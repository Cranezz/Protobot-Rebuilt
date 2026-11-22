"""Tiny Discord REST client for the Protobot server scripts (no gateway: the bot never runs commands, so there is
nothing in Discord anyone can talk to). The token is read from C:/Dev/ProtobotRebuiltV2/Discord/bot_token.txt
(outside the repository) and is never printed."""
import base64
import json
import os
import sys
import time

import requests

PRIVATE = os.path.join("C:/Dev/ProtobotRebuiltV2", "Discord")
TOKEN_FILE = os.path.join(PRIVATE, "bot_token.txt")
STATE_FILE = os.path.join(PRIVATE, "server_state.json")
API = "https://discord.com/api/v10"


def token():
    with open(TOKEN_FILE, encoding="utf-8") as f:
        t = f.read().strip()
    if not t or " " in t or len(t) < 50:
        sys.exit("The bot token file doesn't look like a token (it isn't printed).")
    return t


_session = requests.Session()


def call(method, path, body=None, reason=None, ok404=False):
    headers = {"Authorization": "Bot " + token(), "User-Agent": "DiscordBot (protobot-setup, 1.0)"}
    if reason:
        headers["X-Audit-Log-Reason"] = reason
    for attempt in range(6):
        r = _session.request(method, API + path, headers=headers, json=body, timeout=30)
        if r.status_code == 429:
            wait = float(r.json().get("retry_after", 1.0)) + 0.25
            time.sleep(wait)
            continue
        if r.status_code >= 500:
            time.sleep(1.5 * (attempt + 1))
            continue
        if r.status_code == 404 and ok404:
            return None
        if r.status_code >= 400:
            raise RuntimeError(f"{method} {path} -> {r.status_code}: {r.text[:600]}")
        if r.status_code == 204 or not r.content:
            return None
        return r.json()
    raise RuntimeError(f"{method} {path}: gave up after retries")


def call_with_file(method, path, body, file_path, file_name=None):
    """A message with one attached file (multipart: payload_json + files[0])."""
    headers = {"Authorization": "Bot " + token(), "User-Agent": "DiscordBot (protobot-setup, 1.0)"}
    for attempt in range(4):
        with open(file_path, "rb") as f:
            r = _session.request(method, API + path, headers=headers, timeout=300,
                                 data={"payload_json": json.dumps(body)},
                                 files={"files[0]": (file_name or os.path.basename(file_path), f, "video/mp4")})
        if r.status_code == 429:
            time.sleep(float(r.json().get("retry_after", 1.0)) + 0.25)
            continue
        if r.status_code >= 400:
            raise RuntimeError(f"{method} {path} -> {r.status_code}: {r.text[:600]}")
        return r.json()
    raise RuntimeError("gave up after retries")


def image_data(path):
    with open(path, "rb") as f:
        return "data:image/png;base64," + base64.b64encode(f.read()).decode()


def load_state():
    if os.path.exists(STATE_FILE):
        with open(STATE_FILE, encoding="utf-8") as f:
            return json.load(f)
    return {}


def save_state(state):
    os.makedirs(PRIVATE, exist_ok=True)
    with open(STATE_FILE, "w", encoding="utf-8") as f:
        json.dump(state, f, indent=2)


# Permission bits
P = {name: 1 << bit for name, bit in {
    "CREATE_INSTANT_INVITE": 0, "KICK_MEMBERS": 1, "BAN_MEMBERS": 2, "ADMINISTRATOR": 3, "MANAGE_CHANNELS": 4,
    "MANAGE_GUILD": 5, "ADD_REACTIONS": 6, "VIEW_AUDIT_LOG": 7, "PRIORITY_SPEAKER": 8, "STREAM": 9, "VIEW_CHANNEL": 10,
    "SEND_MESSAGES": 11, "SEND_TTS_MESSAGES": 12, "MANAGE_MESSAGES": 13, "EMBED_LINKS": 14, "ATTACH_FILES": 15,
    "READ_MESSAGE_HISTORY": 16, "MENTION_EVERYONE": 17, "USE_EXTERNAL_EMOJIS": 18, "VIEW_GUILD_INSIGHTS": 19,
    "CONNECT": 20, "SPEAK": 21, "MUTE_MEMBERS": 22, "DEAFEN_MEMBERS": 23, "MOVE_MEMBERS": 24, "USE_VAD": 25,
    "CHANGE_NICKNAME": 26, "MANAGE_NICKNAMES": 27, "MANAGE_ROLES": 28, "MANAGE_WEBHOOKS": 29,
    "MANAGE_GUILD_EXPRESSIONS": 30, "USE_APPLICATION_COMMANDS": 31, "REQUEST_TO_SPEAK": 32, "MANAGE_EVENTS": 33,
    "MANAGE_THREADS": 34, "CREATE_PUBLIC_THREADS": 35, "CREATE_PRIVATE_THREADS": 36, "USE_EXTERNAL_STICKERS": 37,
    "SEND_MESSAGES_IN_THREADS": 38, "USE_EMBEDDED_ACTIVITIES": 39, "MODERATE_MEMBERS": 40, "USE_SOUNDBOARD": 42,
    "CREATE_GUILD_EXPRESSIONS": 43, "CREATE_EVENTS": 44, "USE_EXTERNAL_SOUNDS": 45, "SEND_VOICE_MESSAGES": 46,
    "SEND_POLLS": 49, "USE_EXTERNAL_APPS": 50, "PIN_MESSAGES": 51, "BYPASS_SLOWMODE": 52,
}.items()}


def perms(*names):
    v = 0
    for n in names:
        v |= P[n]
    return v
