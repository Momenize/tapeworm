import asyncio
import json
from collections import deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional, Dict, Any

from rubpy import Client
from rubpy.exceptions import (
    InvalidInput,
    NotRegistered,
    AuthError,
)

# Configuration
SESSION_NAME = "my_session"          # rubpy will create/reuse this session file
PHONE_FILE = "phonenumbers.txt"      # One phone number per line
MESSAGE_FILE = "message.txt"          # The message to send
OUTPUT_FILE = "bulk_send_results.json"
RATE_LIMIT_STATE_FILE = "rate_limit_state.json"
DELAY_BETWEEN_ACCOUNTS = 4           # Seconds between sending to different accounts
MAX_RETRIES_PER_ACCOUNT = 2
DELAY_BETWEEN_ADD_AND_SEND = 1       # Delay after adding contact before sending
MAX_MESSAGES_PER_MINUTE = 15
MAX_MESSAGES_PER_HOUR = 50
MAX_ACCOUNTS_PER_DAY = 500


class RateLimiter:
    """Persisted rolling-window limits for messages and unique accounts."""

    def __init__(self, path: str):
        self.path = Path(path)
        self.message_times = deque()
        self.account_times = deque()
        self._load()

    def _load(self) -> None:
        try:
            with self.path.open("r", encoding="utf-8") as file:
                state = json.load(file)
            self.message_times.extend(float(value) for value in state.get("message_times", []))
            self.account_times.extend(
                (float(value[0]), value[1]) for value in state.get("account_times", [])
            )
        except (FileNotFoundError, json.JSONDecodeError, TypeError, ValueError, KeyError):
            self.message_times.clear()
            self.account_times.clear()
        self._prune(datetime.now(timezone.utc).timestamp())

    def _save(self) -> None:
        with self.path.open("w", encoding="utf-8") as file:
            json.dump({
                "message_times": list(self.message_times),
                "account_times": list(self.account_times),
            }, file, indent=2)

    def _prune(self, now: float) -> None:
        while self.message_times and now - self.message_times[0] >= 3600:
            self.message_times.popleft()
        while self.account_times and now - self.account_times[0][0] >= 86400:
            self.account_times.popleft()

    async def wait_for_message_slot(self) -> None:
        while True:
            now = datetime.now(timezone.utc).timestamp()
            self._prune(now)
            minute_count = sum(now - timestamp < 60 for timestamp in self.message_times)
            hour_count = len(self.message_times)
            if minute_count < MAX_MESSAGES_PER_MINUTE and hour_count < MAX_MESSAGES_PER_HOUR:
                self.message_times.append(now)
                self._save()
                return

            waits = []
            if minute_count >= MAX_MESSAGES_PER_MINUTE:
                waits.append(60 - (now - self.message_times[-MAX_MESSAGES_PER_MINUTE]))
            if hour_count >= MAX_MESSAGES_PER_HOUR:
                waits.append(3600 - (now - self.message_times[0]))
            wait_time = max(0.1, max(waits))
            print(f"  Rate limit reached; waiting {wait_time:.1f}s before sending...")
            await asyncio.sleep(wait_time)

    def reserve_account(self, phone: str) -> bool:
        now = datetime.now(timezone.utc).timestamp()
        self._prune(now)
        if any(account == phone for _, account in self.account_times):
            return True
        if len(self.account_times) >= MAX_ACCOUNTS_PER_DAY:
            return False
        self.account_times.append((now, phone))
        self._save()
        return True



def read_phone_numbers(path: str) -> list[str]:
    """Read one phone number per line, ignoring blanks and comments."""
    phone_numbers = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            # Clean up phone number format (remove spaces, dashes, etc.)
            cleaned = ''.join(c for c in line if c.isdigit() or c == '+')
            if cleaned:
                phone_numbers.append(cleaned)
    return phone_numbers


def read_message(path: str) -> str:
    """Read the message to send from a file."""
    with open(path, "r", encoding="utf-8") as f:
        return f.read().strip()


async def add_contact_and_get_guid(client: Client, phone: str) -> Optional[str]:
    """
    Add a phone number to the address book and return the user's GUID.
    This is necessary because Rubika requires contacts to be added before messaging.
    """
    try:
        # Add the phone number to address book with a generic name
        response = await client.add_address_book(
            phone=phone,
            first_name="User",
            last_name=phone[-4:]  # Use last 4 digits of phone as last name
        )
        
        # Extract the user GUID from the response
        if response:
            if isinstance(response, dict):
                # Try nested structure first
                user_guid = (
                    response.get("chat_update", {}).get("object_guid") or
                    response.get("user", {}).get("user_guid") or
                    response.get("user_guid") or
                    response.get("guid") or
                    response.get("object_guid")
                )
            else:
                # Try object attributes
                user_guid = (
                    getattr(response, "user_guid", None) or 
                    getattr(response, "guid", None) or 
                    getattr(response, "object_guid", None)
                )
            
            if user_guid:
                return user_guid
        
        return None
        
    except Exception as e:
        print(f"  Error adding contact {phone}: {e}")
        return None


async def send_message_to_account(
    client: Client,
    phone: str,
    message: str,
    rate_limiter: RateLimiter,
) -> Dict[str, Any]:
    """Send a message to a single account."""
    result = {
        "phone": phone,
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "success": False,
        "message": message,
        "error": None,
        "message_id": None,
    }
    
    try:
        # First, add the contact to the address book
        print(f"  Adding {phone} to address book...")
        user_guid = await add_contact_and_get_guid(client, phone)
        
        if not user_guid:
            result["error"] = "Failed to add contact to address book"
            return result
        
        # Small delay to ensure the contact is registered
        await asyncio.sleep(DELAY_BETWEEN_ADD_AND_SEND)
        
        # Now send the message
        print(f"  Sending message to {phone}...")
        await rate_limiter.wait_for_message_slot()
        response = await client.send_message(
            object_guid=user_guid,
            text=message
        )
        
        if response:
            # Extract from nested message_update structure
            if isinstance(response, dict):
                message_id = (
                    response.get("message_update", {}).get("message_id") or
                    response.get("message_id") or
                    response.get("id")
                )
            else:
                # Try object attributes
                message_id = (
                    getattr(response, "message_id", None) or 
                    getattr(response, "id", None)
                )
            
            if message_id:
                result["success"] = True
                result["message_id"] = message_id
            else:
                result["error"] = "Message sent but no message_id returned"
        else:
            result["error"] = "No response from server"
            
    except (InvalidInput, NotRegistered, AuthError) as e:
        result["error"] = f"Auth/API error: {e}"
    except Exception as e:
        result["error"] = f"Unexpected error: {e}"
    
    return result


async def send_with_retry(
    client: Client,
    phone: str,
    message: str,
    rate_limiter: RateLimiter,
) -> Dict[str, Any]:
    """Send a message with retry logic."""
    for attempt in range(MAX_RETRIES_PER_ACCOUNT + 1):
        result = await send_message_to_account(client, phone, message, rate_limiter)
        if result["success"]:
            return result
        
        if attempt < MAX_RETRIES_PER_ACCOUNT:
            wait_time = 2 ** attempt  # Exponential backoff: 1s, 2s, 4s
            print(f"  Retry {attempt + 1}/{MAX_RETRIES_PER_ACCOUNT} for {phone} in {wait_time}s...")
            await asyncio.sleep(wait_time)
        else:
            print(f"  Failed to send to {phone} after {MAX_RETRIES_PER_ACCOUNT} retries")
            return result


async def main():
    """Main function to send bulk messages."""
    # Read phone numbers
    phone_numbers = read_phone_numbers(PHONE_FILE)
    if not phone_numbers:
        print(f"No phone numbers found in {PHONE_FILE}")
        return
    
    # Read message
    try:
        message = read_message(MESSAGE_FILE)
        if not message:
            print(f"Message is empty in {MESSAGE_FILE}")
            return
    except FileNotFoundError:
        print(f"Message file {MESSAGE_FILE} not found")
        return
    
    print(f"Loaded {len(phone_numbers)} phone numbers")
    print(f"Message length: {len(message)} characters")
    print(f"Message: {message[:100]}{'...' if len(message) > 100 else ''}")
    print("-" * 50)
    
    # Results storage
    all_results = []
    successful = 0
    failed = 0
    rate_limiter = RateLimiter(RATE_LIMIT_STATE_FILE)
    
    # Create session and start sending
    async with Client(SESSION_NAME) as client:
        for i, phone in enumerate(phone_numbers, 1):
            print(f"[{i}/{len(phone_numbers)}] Sending to: {phone}")

            if not rate_limiter.reserve_account(phone):
                print(f"  24-hour account limit reached ({MAX_ACCOUNTS_PER_DAY}); stopping")
                break
            
            result = await send_with_retry(client, phone, message, rate_limiter)
            all_results.append(result)
            
            if result["success"]:
                successful += 1
                print(f"  ✓ Sent successfully (message_id: {result['message_id']})")
            else:
                failed += 1
                print(f"  ✗ Failed: {result['error']}")
            
            # Progress report
            if i % 10 == 0:
                print(f"  Progress: {successful} successful, {failed} failed out of {i} processed")
            
            # Delay between sends (except after the last one)
            if i < len(phone_numbers):
                await asyncio.sleep(DELAY_BETWEEN_ACCOUNTS)
    
    # Save results
    summary = {
        "total_phones": len(phone_numbers),
        "successful": successful,
        "failed": failed,
        "message": message,
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "results": all_results,
    }
    
    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        json.dump(summary, f, ensure_ascii=False, indent=2)
    
    print("-" * 50)
    print(f"Done! Results saved to {OUTPUT_FILE}")
    print(f"Summary: {successful} successful, {failed} failed out of {len(phone_numbers)} total")


if __name__ == "__main__":
    asyncio.run(main())