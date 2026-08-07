#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import sys
import time
import urllib.error
import urllib.request

BASE = os.environ.get("JELLYFIN_URL", "http://127.0.0.1:8096").rstrip("/")
CLIENT_HEADER = 'MediaBrowser Client="Community%20CI", DeviceId="community-ci", Device="GitHub%20Actions", Version="1.1.0.0"'
ADMIN_NAME = "community-admin"
ADMIN_PASSWORD = "community-admin-password"
USER_NAME = "community-user"
USER_PASSWORD = "community-user-password"


def call(method: str, path: str, body=None, token: str | None = None, expected=(200, 204), raw=False):
    data = None
    headers = {"Accept": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["Content-Type"] = "application/json"
    headers["Authorization"] = CLIENT_HEADER + (f", Token={token}" if token else "")
    request = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            payload = response.read()
            status = response.status
            content_type = response.headers.get("content-type", "")
    except urllib.error.HTTPError as exc:
        payload = exc.read()
        status = exc.code
        content_type = exc.headers.get("content-type", "")
    if status not in expected:
        raise AssertionError(f"{method} {path}: expected {expected}, got {status}: {payload[:1000]!r}")
    if raw:
        return status, payload, content_type
    if not payload:
        return None
    return json.loads(payload.decode("utf-8"))


def wait_for_server():
    deadline = time.time() + 180
    last_error = None
    while time.time() < deadline:
        try:
            status, payload, _ = call("GET", "/System/Info/Public", expected=(200,), raw=True)
            if status == 200 and payload:
                return
        except Exception as exc:  # noqa: BLE001 - diagnostics for CI startup
            last_error = exc
        time.sleep(2)
    raise RuntimeError(f"Jellyfin did not start within 180 seconds: {last_error}")


def authenticate(username: str, password: str) -> str:
    result = call("POST", "/Users/AuthenticateByName", {"Username": username, "Pw": password}, expected=(200,))
    token = result.get("AccessToken") or result.get("accessToken")
    if not token:
        raise AssertionError(f"No access token returned for {username}")
    return token


def main() -> int:
    wait_for_server()
    call("POST", "/Startup/Configuration", {
        "UICulture": "es-ES",
        "MetadataCountryCode": "ES",
        "PreferredMetadataLanguage": "es",
    }, expected=(204,))
    call("POST", "/Startup/User", {"Name": ADMIN_NAME, "Password": ADMIN_PASSWORD}, expected=(204,))
    call("POST", "/Startup/RemoteAccess", {"EnableRemoteAccess": False, "EnableAutomaticPortMapping": False}, expected=(204,))
    call("POST", "/Startup/Complete", expected=(204,))

    admin_token = authenticate(ADMIN_NAME, ADMIN_PASSWORD)

    _, index_bytes, content_type = call("GET", "/web/index.html", token=admin_token, expected=(200,), raw=True)
    index_text = index_bytes.decode("utf-8", errors="replace")
    assert "data-jellyfin-community-bootstrap" in index_text, "Community bootstrap was not injected into Jellyfin Web index"
    assert "text/html" in content_type.lower(), content_type

    _, bootstrap_bytes, _ = call("GET", "/web/ConfigurationPage?name=CommunityBootstrap", token=admin_token, expected=(200,), raw=True)
    bootstrap = bootstrap_bytes.decode("utf-8", errors="replace")
    assert "JellyfinCommunityBootstrap" in bootstrap
    assert "customMenuOptions" in bootstrap

    _, controller_bytes, _ = call("GET", "/web/ConfigurationPage?name=CommunityPageController", token=admin_token, expected=(200,), raw=True)
    controller = controller_bytes.decode("utf-8", errors="replace")
    assert "export default class CommunityPageController" in controller

    _, page_bytes, _ = call("GET", "/web/ConfigurationPage?name=Community", token=admin_token, expected=(200,), raw=True)
    page = page_bytes.decode("utf-8", errors="replace")
    assert 'data-controller="CommunityPageController"' in page
    assert "<script" not in page.lower(), "Community page must not rely on inline script execution"

    me_admin = call("GET", "/Community/api/v1/me", token=admin_token, expected=(200,))
    assert me_admin["isAdministrator"] is True
    assert me_admin["isModerator"] is True

    categories = call("GET", "/Community/api/v1/categories", token=admin_token, expected=(200,))
    assert len(categories) >= 3, categories

    created_user = call("POST", "/Users/New", {"Name": USER_NAME, "Password": USER_PASSWORD}, token=admin_token, expected=(200,))
    assert created_user.get("Name") == USER_NAME or created_user.get("name") == USER_NAME
    user_token = authenticate(USER_NAME, USER_PASSWORD)

    me_user = call("GET", "/Community/api/v1/me", token=user_token, expected=(200,))
    assert me_user["isAdministrator"] is False
    assert me_user["isModerator"] is False

    user_categories = call("GET", "/Community/api/v1/categories", token=user_token, expected=(200,))
    writable = next((category for category in user_categories if not category["isArchived"] and not category["isReadOnly"]), None)
    assert writable is not None, user_categories

    thread = call("POST", "/Community/api/v1/threads", {
        "categoryId": writable["id"],
        "kind": 0,
        "title": "Community E2E thread",
        "body": "Mensaje creado por la prueba real de Jellyfin 10.10.7.",
        "itemId": None,
        "itemName": None,
        "tags": ["e2e"],
        "containsSpoiler": False,
        "spoilerItemId": None,
        "spoilerLabel": None,
        "poll": None,
    }, token=user_token, expected=(201,))
    thread_id = thread["thread"]["id"]
    first_post_id = thread["firstPost"]["id"]

    listed = call("GET", "/Community/api/v1/threads?page=1&pageSize=25", token=user_token, expected=(200,))
    assert any(item["id"] == thread_id for item in listed["items"])

    call("PUT", f"/Community/api/v1/posts/{first_post_id}/reaction", {"reaction": "like"}, token=user_token, expected=(204,))
    posts = call("GET", f"/Community/api/v1/threads/{thread_id}/posts?page=1&pageSize=25", token=user_token, expected=(200,))
    first = next(post for post in posts["items"] if post["id"] == first_post_id)
    assert first["reactions"].get("like") == 1

    time.sleep(11)
    reply = call("POST", f"/Community/api/v1/threads/{thread_id}/posts", {
        "body": "Respuesta E2E del usuario normal.",
        "parentPostId": first_post_id,
        "containsSpoiler": False,
        "spoilerItemId": None,
        "spoilerLabel": None,
    }, token=user_token, expected=(201,))
    assert reply["threadId"] == thread_id

    call("GET", "/Community/api/v1/admin/stats", token=user_token, expected=(403,))
    stats = call("GET", "/Community/api/v1/admin/stats", token=admin_token, expected=(200,))
    assert stats["threads"] >= 1 and stats["posts"] >= 2

    integration = call("GET", "/Community/api/v1/admin/web-integration", token=admin_token, expected=(200,))
    assert integration["indexRequestsSeen"] >= 1, integration
    assert integration["indexResponsesTransformed"] >= 1, integration
    assert not integration.get("lastError"), integration

    print(json.dumps({
        "adminToken": admin_token,
        "userToken": user_token,
        "threadId": thread_id,
        "firstPostId": first_post_id,
        "integration": integration,
    }))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:  # noqa: BLE001 - CI should print actionable details
        print(f"E2E FAILURE: {exc}", file=sys.stderr)
        raise
