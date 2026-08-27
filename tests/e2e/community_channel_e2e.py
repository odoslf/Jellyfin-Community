#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("JELLYFIN_URL", "http://127.0.0.1:8096").rstrip("/")
CLIENT_HEADER = 'MediaBrowser Client="Community%20Channel%20CI", DeviceId="community-channel-ci", Device="GitHub%20Actions", Version="1.6.0.0"'
USERS = (
    ("community-admin", "community-admin-password"),
    ("community-user", "community-user-password"),
)


def request(method: str, path: str, body=None, token: str | None = None):
    data = None
    headers = {"Accept": "application/json", "Authorization": CLIENT_HEADER + (f", Token={token}" if token else "")}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            payload = response.read()
            status = response.status
    except urllib.error.HTTPError as exc:
        payload = exc.read()
        status = exc.code
    if status not in (200, 204):
        raise AssertionError(f"{method} {path}: got {status}: {payload[:1000]!r}")
    return json.loads(payload.decode("utf-8")) if payload else None


def authenticate(username: str, password: str) -> str:
    result = request("POST", "/Users/AuthenticateByName", {"Username": username, "Pw": password})
    token = result.get("AccessToken") or result.get("accessToken")
    if not token:
        raise AssertionError(f"No access token for {username}")
    return token


def pick(obj: dict, name: str):
    return obj.get(name) if name in obj else obj.get(name[0].lower() + name[1:])


def validate_user(username: str, password: str) -> dict:
    token = authenticate(username, password)
    me = request("GET", "/Users/Me", token=token)
    user_id = pick(me, "Id")
    if not user_id:
        raise AssertionError(f"No Jellyfin user id for {username}: {me}")

    channels = request("GET", "/Channels?" + urllib.parse.urlencode({"userId": user_id}), token=token)
    channel_items = pick(channels, "Items") or []
    forum = next((item for item in channel_items if pick(item, "Name") == "Foro"), None)
    if forum is None:
        raise AssertionError(f"Native Foro channel not visible for {username}: {channel_items}")

    channel_id = pick(forum, "Id")
    root = request("GET", f"/Users/{user_id}/Items?" + urllib.parse.urlencode({"ParentId": channel_id}), token=token)
    root_items = pick(root, "Items") or []
    entry = next((item for item in root_items if pick(item, "Name") == "Acceder al Foro Comunitario"), None)
    if entry is None:
        raise AssertionError(f"Native Forum entry missing for {username}: {root_items}")

    # Jellyfin serializes IChannel folder items as ChannelFolderItem DTOs.
    # IsFolder is the stable semantic flag exposed to clients.
    if pick(entry, "IsFolder") is not True:
        raise AssertionError(f"Forum entry should be folder-like for {username}: {entry}")
    if pick(entry, "Type") != "ChannelFolderItem":
        raise AssertionError(f"Unexpected Jellyfin channel folder DTO type for {username}: {entry}")
    if pick(entry, "ChannelName") != "Foro":
        raise AssertionError(f"Forum entry lost channel association for {username}: {entry}")

    return {
        "username": username,
        "userId": user_id,
        "channelId": channel_id,
        "entryId": pick(entry, "Id"),
        "entryType": pick(entry, "Type"),
        "isFolder": pick(entry, "IsFolder"),
    }


def main() -> None:
    evidence = [validate_user(username, password) for username, password in USERS]
    print(json.dumps({"status": "passed", "users": evidence}, ensure_ascii=False))


if __name__ == "__main__":
    main()
