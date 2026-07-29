"""
Rubika Channel Message Extractor
---------------------------------
Reads channel IDs (usernames or GUIDs) from rubika_channels.txt (one per line),
fetches the last 50 messages from each channel, and saves the text content,
image captions, and timestamps to a JSON file.

Requirements:
    pip install rubpy

Authentication:
    Rubika's channel-reading API requires a logged-in user session (there is
    no public read-only API for arbitrary channel history). The first time
    you run this script, rubpy will ask for your phone number and a login
    code sent to your Rubika app, then it will save a session file
    (e.g. "my_session.session") in the same folder so you won't have to log
    in again on future runs.

Usage:
    1. Put the channel IDs you want to scrape in rubika_channels.txt, one
       per line. These can be public channel usernames (e.g. "my_channel",
       with or without the leading @) or channel GUIDs.
    2. Run: python rubika_extractor.py
    3. Output is written to rubika_messages.json
"""

import asyncio
import json
from datetime import datetime, timezone

from rubpy import Client
from rubpy.exceptions import (
    InvalidInput,
    NotRegistered,
)

SESSION_NAME = "my_session"          # rubpy will create/reuse this session file
CHANNELS_FILE = "rubika_channels.txt"
OUTPUT_FILE = "rubika_messages.json"
MESSAGES_PER_CHANNEL = 50


def read_channel_ids(path: str) -> list[str]:
    """Read one channel id per line, ignoring blanks and comments."""
    channel_ids = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            channel_ids.append(line)
    return channel_ids


def extract_text_and_caption(message) -> tuple[str | None, str | None]:
    """
    Pull plain text and (if present) an image/media caption out of a
    rubpy message (works whether it's an attribute-style object or a plain
    dict). rubpy normally exposes:
        message.text        -> text of a plain text message
        message.file        -> present when the message has attached media
        message.file.caption (or similar) -> caption for that media
    Field names can vary by rubpy version, so this checks a few likely
    spots defensively.
    """
    text = _get_field(message, "text")
    caption = _get_field(message, "caption")

    file_obj = _get_field(message, "file") or _get_field(message, "file_inline")
    if not caption and file_obj is not None:
        caption = _get_field(file_obj, "caption")

    return text, caption


def format_timestamp(raw_time) -> str | None:
    """Convert whatever timestamp rubpy gives us to an ISO 8601 string (UTC)."""
    if raw_time is None:
        return None
    try:
        ts = int(raw_time)
        return datetime.fromtimestamp(ts, tz=timezone.utc).isoformat()
    except (ValueError, TypeError, OSError):
        return str(raw_time)


def _get_field(obj, name, default=None):
    """Read `name` off obj whether it's an attribute-style object or a plain dict."""
    if obj is None:
        return default
    if isinstance(obj, dict):
        return obj.get(name, default)
    return getattr(obj, name, default)


async def fetch_last_n_messages(client: Client, guid: str, n: int, seed_message_id) -> list:
    """
    Walk backward through a channel's history using get_messages_interval,
    which returns roughly the last 25 messages ending at `middle_message_id`.
    Rubika's API requires a real starting message id (it rejects None with
    INVALID_INPUT), so the caller must supply the channel's last message id
    as `seed_message_id`. We then keep calling with progressively older
    message IDs until we have `n` unique messages or run out of history.
    """
    if not seed_message_id:
        return []

    collected = {}
    middle_id = seed_message_id

    for _ in range(10):  # safety cap: at most 10 pages (~250 messages)
        if len(collected) >= n:
            break

        response = await client.get_messages_interval(guid, middle_message_id=middle_id)
        batch = _get_field(response, "messages", response if isinstance(response, list) else None)
        if not batch:
            break

        new_ids = []
        for msg in batch:
            mid = _get_field(msg, "message_id")
            if mid is not None and mid not in collected:
                collected[mid] = msg
                new_ids.append(mid)

        if not new_ids:
            # No new messages came back, so we've hit the start of the channel.
            break

        # Move the window further back in history for the next call.
        oldest_id = min(new_ids, key=lambda i: int(i))
        if oldest_id == middle_id:
            break
        middle_id = oldest_id

    # Sort newest-first and trim to the requested count.
    ordered = sorted(collected.values(), key=lambda m: int(_get_field(m, "message_id", 0)), reverse=True)
    return ordered[:n]


async def fetch_channel_messages(client: Client, channel_id: str) -> dict:
    """Fetch the last N messages for a single channel and shape them for JSON."""
    channel_id = channel_id.lstrip("@")
    result = {
        "channel_id": channel_id,
        "status": "ok",
        "messages": [],
    }

    try:
        # Resolve the channel's guid from its username/id first.
        info = await client.get_object_by_username(channel_id)
        guid = _get_field(info, "object_guid") or _get_field(info, "guid")
        if guid is None:
            # Fall back to treating the input itself as a guid.
            guid = channel_id

        # The "chat" sub-object carries the channel's most recent message id,
        # which we need to seed get_messages_interval (it has no "give me
        # the latest" default and rejects an empty/None id).
        chat = _get_field(info, "chat")
        seed_message_id = (
            _get_field(chat, "last_message_id")
            or _get_field(info, "last_message_id")
        )
        if seed_message_id is None:
            last_message = _get_field(chat, "last_message") or _get_field(info, "last_message")
            seed_message_id = _get_field(last_message, "message_id")

        if seed_message_id is None:
            result["status"] = "error: could not determine a starting message id for this channel"
            return result

        messages = await fetch_last_n_messages(client, guid, MESSAGES_PER_CHANNEL, seed_message_id)

        for msg in messages:
            text, caption = extract_text_and_caption(msg)
            raw_time = _get_field(msg, "time") or _get_field(msg, "date")

            # Skip messages that have neither text nor a caption (e.g. pure
            # stickers/voice notes with nothing to extract).
            if not text and not caption:
                continue

            result["messages"].append(
                {
                    "message_id": _get_field(msg, "message_id"),
                    "datetime_utc": format_timestamp(raw_time),
                    "text": text,
                    "caption": caption,
                }
            )

    except (InvalidInput, NotRegistered) as e:
        result["status"] = f"error: {e}"
    except Exception as e:  # noqa: BLE001 - report any unexpected API error per-channel
        result["status"] = f"error: {e}"

    return result


async def main():
    channel_ids = read_channel_ids(CHANNELS_FILE)
    if not channel_ids:
        print(f"No channel IDs found in {CHANNELS_FILE}")
        return

    all_results = []

    async with Client(SESSION_NAME) as client:
        for channel_id in channel_ids:
            print(f"Fetching last {MESSAGES_PER_CHANNEL} messages from: {channel_id}")
            channel_result = await fetch_channel_messages(client, channel_id)
            all_results.append(channel_result)
            # Small delay to avoid hammering the API across many channels
            await asyncio.sleep(1)

    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        json.dump(all_results, f, ensure_ascii=False, indent=2)

    print(f"Done. Saved results to {OUTPUT_FILE}")


if __name__ == "__main__":
    asyncio.run(main())
