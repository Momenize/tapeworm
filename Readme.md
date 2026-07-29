# Tapeworm Message Extractor

1. ## Rubikaworm: 
    - Reads channel IDs (usernames or GUIDs) from rubika_channels.txt (one per line)
    - fetches the last 50 messages from each channel, and saves the text content, image captions, and timestamps to a JSON file.  

    > * ### Requirements:
        >> pip install rubpy  
    > * ### Authentication:
        >> Rubika's channel-reading API requires a logged-in user session (there is no public read-only API for arbitrary channel history). 
        >>
        >> The first time you run this script, rubpy will ask for your phone number and a login code sent to your Rubika app, then it will save a session file (e.g. "my_session.session") in the same folder so you won't have to log in again on future runs.
    > * ### Usage:
        >> 1. Put the channel IDs you want to scrape in rubika_channels.txt, one per line. These can be public channel usernames (e.g. "my_channel", with or without the leading @) or channel GUIDs.
        >> 2. Run: python rubika_extractor.py
        >> 3. Output is written to rubika_messages.json
2. ## Teleworm:
    > To be added...
