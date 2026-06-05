from __future__ import annotations

import hashlib
import json
import secrets
from datetime import datetime, timedelta
from typing import Any

from sqlalchemy import func
from sqlalchemy.orm import Session

from apps.web.models import DesktopDevice, DesktopSession, SubscriptionPlan, User, UserSubscription
from apps.web.services.auth import authenticate_user
from apps.web.settings import get_settings


ACTIVE_SUBSCRIPTION_STATUSES = {"Active", "Trialing"}
ACTIVE_DEVICE_STATUS = "Active"
REVOKED_DEVICE_STATUS = "Revoked"
ACTIVE_SESSION_STATUS = "Active"
ENDED_SESSION_STATUS = "Ended"

LOCAL_TEST_HANDLES = {"admin", "jsibitoye", "admin@roleaxis.local", "jsibitoye@roleaxis.local"}
VAULT_STORAGE_MODES = (
    "Local Only",
    "Local + Cloud Metadata",
    "Local + Encrypted Cloud Backup",
)

PLAN_DEFINITIONS = [
    {
        "name": "Free",
        "price": "$0",
        "max_devices": 1,
        "max_active_sessions": 1,
        "features": ["Web dashboard", "Evidence workspace preview", "Vault local-only settings"],
    },
    {
        "name": "Pro",
        "price": "$29/mo",
        "max_devices": 3,
        "max_active_sessions": 2,
        "features": [
            "Interview Assistant desktop license",
            "Vault Local Agent context",
            "Evidence Scanner",
            "Interview context export",
        ],
    },
    {
        "name": "Business",
        "price": "$99/mo",
        "max_devices": 10,
        "max_active_sessions": 5,
        "features": [
            "Interview Assistant desktop license",
            "Vault Local Agent context",
            "Evidence Scanner",
            "Team-ready device controls",
            "Priority rollout path",
        ],
    },
]


class DesktopLicenseError(Exception):
    def __init__(self, message: str, *, status_code: int = 400, code: str = "license_error") -> None:
        self.message = message
        self.status_code = status_code
        self.code = code
        super().__init__(message)


def utcnow() -> datetime:
    return datetime.utcnow()


def hash_desktop_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def create_desktop_token() -> str:
    return secrets.token_urlsafe(48)


def ensure_subscription_plans(db: Session) -> None:
    changed = False
    for definition in PLAN_DEFINITIONS:
        plan = db.query(SubscriptionPlan).filter(SubscriptionPlan.name == definition["name"]).first()
        features_json = json.dumps(definition["features"])
        if plan is None:
            db.add(
                SubscriptionPlan(
                    name=definition["name"],
                    price=definition["price"],
                    max_devices=definition["max_devices"],
                    max_active_sessions=definition["max_active_sessions"],
                    features_json=features_json,
                )
            )
            changed = True
            continue

        if (
            plan.price != definition["price"]
            or plan.max_devices != definition["max_devices"]
            or plan.max_active_sessions != definition["max_active_sessions"]
            or plan.features_json != features_json
        ):
            plan.price = definition["price"]
            plan.max_devices = definition["max_devices"]
            plan.max_active_sessions = definition["max_active_sessions"]
            plan.features_json = features_json
            changed = True

    if changed:
        db.commit()


def seed_local_test_subscriptions(db: Session) -> None:
    settings = get_settings()
    if not settings.seed_local_pro_subscriptions:
        return

    ensure_subscription_plans(db)
    pro_plan = plan_by_name(db, "Pro")
    if pro_plan is None:
        return

    for user in db.query(User).all():
        handle = (user.email or "").strip().lower()
        if handle not in LOCAL_TEST_HANDLES:
            continue
        existing = (
            db.query(UserSubscription)
            .filter(UserSubscription.user_id == user.id)
            .order_by(UserSubscription.created_at.desc())
            .first()
        )
        if existing is not None:
            if existing.status == "Active" and existing.plan.name == "Free":
                existing.plan_id = pro_plan.id
                existing.updated_at = utcnow()
            continue
        db.add(
            UserSubscription(
                user_id=user.id,
                plan_id=pro_plan.id,
                status="Active",
                current_period_start=utcnow(),
                current_period_end=utcnow() + timedelta(days=365),
            )
        )
    db.commit()


def plan_by_name(db: Session, name: str) -> SubscriptionPlan | None:
    return db.query(SubscriptionPlan).filter(SubscriptionPlan.name == name).first()


def plan_features(plan: SubscriptionPlan | None) -> list[str]:
    if plan is None:
        return []
    try:
        parsed = json.loads(plan.features_json or "[]")
    except json.JSONDecodeError:
        return []
    return [str(item) for item in parsed if str(item).strip()]


def is_subscription_active(subscription: UserSubscription | None) -> bool:
    if subscription is None:
        return False
    if subscription.status not in ACTIVE_SUBSCRIPTION_STATUSES:
        return False
    return subscription.current_period_end is None or subscription.current_period_end > utcnow()


def active_subscription(db: Session, user: User) -> UserSubscription | None:
    subscriptions = (
        db.query(UserSubscription)
        .filter(UserSubscription.user_id == user.id)
        .order_by(UserSubscription.created_at.desc())
        .all()
    )
    for subscription in subscriptions:
        if is_subscription_active(subscription):
            return subscription
    return subscriptions[0] if subscriptions else None


def ensure_user_subscription(db: Session, user: User) -> UserSubscription:
    ensure_subscription_plans(db)
    existing = active_subscription(db, user)
    if existing is not None:
        return existing

    settings = get_settings()
    handle = (user.email or "").strip().lower()
    default_plan_name = "Pro" if settings.seed_local_pro_subscriptions and handle in LOCAL_TEST_HANDLES else "Free"
    plan = plan_by_name(db, default_plan_name) or plan_by_name(db, "Free")
    if plan is None:
        raise DesktopLicenseError("Subscription plans are not available.", status_code=500, code="plans_missing")

    subscription = UserSubscription(
        user_id=user.id,
        plan_id=plan.id,
        status="Active",
        current_period_start=utcnow(),
        current_period_end=utcnow() + timedelta(days=365),
    )
    db.add(subscription)
    db.commit()
    db.refresh(subscription)
    return subscription


def active_device_count(db: Session, user: User) -> int:
    return (
        db.query(func.count(DesktopDevice.id))
        .filter(DesktopDevice.user_id == user.id, DesktopDevice.status == ACTIVE_DEVICE_STATUS)
        .scalar()
        or 0
    )


def active_session_count(db: Session, user: User) -> int:
    return (
        db.query(func.count(DesktopSession.id))
        .filter(DesktopSession.user_id == user.id, DesktopSession.status == ACTIVE_SESSION_STATUS)
        .scalar()
        or 0
    )


def end_active_sessions_for_device(db: Session, device: DesktopDevice) -> None:
    sessions = (
        db.query(DesktopSession)
        .filter(DesktopSession.device_id == device.id, DesktopSession.status == ACTIVE_SESSION_STATUS)
        .all()
    )
    now = utcnow()
    for session in sessions:
        session.status = ENDED_SESSION_STATUS
        session.ended_at = now


def revoke_device(db: Session, user: User, device_id: int) -> DesktopDevice:
    device = (
        db.query(DesktopDevice)
        .filter(DesktopDevice.id == device_id, DesktopDevice.user_id == user.id)
        .first()
    )
    if device is None:
        raise DesktopLicenseError("Device not found.", status_code=404, code="device_not_found")
    device.status = REVOKED_DEVICE_STATUS
    device.revoked_at = utcnow()
    end_active_sessions_for_device(db, device)
    db.commit()
    db.refresh(device)
    return device


def subscription_payload(
    db: Session,
    user: User,
    subscription: UserSubscription | None = None,
) -> dict[str, Any]:
    subscription = subscription or ensure_user_subscription(db, user)
    plan = subscription.plan
    return {
        "status": subscription.status,
        "is_active": is_subscription_active(subscription),
        "current_period_start": subscription.current_period_start.isoformat(),
        "current_period_end": subscription.current_period_end.isoformat() if subscription.current_period_end else None,
        "plan": {
            "id": plan.id,
            "name": plan.name,
            "price": plan.price,
            "max_devices": plan.max_devices,
            "max_active_sessions": plan.max_active_sessions,
            "features": plan_features(plan),
        },
        "usage": {
            "active_devices": active_device_count(db, user),
            "active_sessions": active_session_count(db, user),
        },
    }


def desktop_login(
    db: Session,
    *,
    login: str,
    password: str,
    device_name: str,
    device_fingerprint: str,
    platform: str,
    app_version: str,
) -> dict[str, Any]:
    user = authenticate_user(db, login, password)
    if user is None:
        raise DesktopLicenseError("Invalid account credentials.", status_code=401, code="invalid_credentials")

    if not device_fingerprint.strip():
        raise DesktopLicenseError("Device fingerprint is required.", status_code=400, code="missing_device_fingerprint")

    subscription = ensure_user_subscription(db, user)
    if not is_subscription_active(subscription):
        raise DesktopLicenseError("Subscription is inactive.", status_code=403, code="inactive_subscription")

    device = (
        db.query(DesktopDevice)
        .filter(
            DesktopDevice.user_id == user.id,
            DesktopDevice.device_fingerprint == device_fingerprint.strip(),
        )
        .first()
    )
    if device and device.status == REVOKED_DEVICE_STATUS:
        raise DesktopLicenseError("This device has been revoked.", status_code=403, code="device_revoked")

    now = utcnow()
    if device is None:
        if active_device_count(db, user) >= subscription.plan.max_devices:
            raise DesktopLicenseError("Device limit reached for this plan.", status_code=403, code="device_limit")
        device = DesktopDevice(
            user_id=user.id,
            device_name=device_name.strip() or "Local workstation",
            device_fingerprint=device_fingerprint.strip(),
            platform=platform.strip(),
            app_version=app_version.strip(),
            status=ACTIVE_DEVICE_STATUS,
            last_seen_at=now,
        )
        db.add(device)
        db.flush()
    else:
        device.device_name = device_name.strip() or device.device_name
        device.platform = platform.strip()
        device.app_version = app_version.strip()
        device.status = ACTIVE_DEVICE_STATUS
        device.last_seen_at = now
        end_active_sessions_for_device(db, device)
        db.flush()

    if active_session_count(db, user) >= subscription.plan.max_active_sessions:
        raise DesktopLicenseError("Active desktop session limit reached.", status_code=403, code="session_limit")

    token = create_desktop_token()
    desktop_session = DesktopSession(
        user_id=user.id,
        device_id=device.id,
        session_token_hash=hash_desktop_token(token),
        status=ACTIVE_SESSION_STATUS,
        started_at=now,
        last_heartbeat_at=now,
    )
    db.add(desktop_session)
    db.commit()
    db.refresh(device)
    db.refresh(desktop_session)

    return {
        "session_token": token,
        "user": {"id": user.id, "email": user.email, "full_name": user.full_name, "role": user.role},
        "device": device_payload(device),
        "session": {
            "id": desktop_session.id,
            "status": desktop_session.status,
            "started_at": desktop_session.started_at.isoformat(),
            "last_heartbeat_at": desktop_session.last_heartbeat_at.isoformat(),
        },
        "subscription": subscription_payload(db, user, subscription),
        "vault": {
            "storage_mode": user.vault_storage_mode,
            "large_documents_upload_by_default": False,
        },
    }


def active_session_for_token(
    db: Session,
    token: str,
    *,
    device_fingerprint: str = "",
) -> DesktopSession:
    if not token:
        raise DesktopLicenseError("Desktop session token is required.", status_code=401, code="missing_token")

    session = (
        db.query(DesktopSession)
        .filter(DesktopSession.session_token_hash == hash_desktop_token(token))
        .first()
    )
    if session is None:
        raise DesktopLicenseError("Desktop session is not active.", status_code=401, code="inactive_session")

    device = session.device
    if device.status != ACTIVE_DEVICE_STATUS or device.revoked_at is not None:
        if session.status == ACTIVE_SESSION_STATUS:
            session.status = ENDED_SESSION_STATUS
            session.ended_at = utcnow()
            db.commit()
        raise DesktopLicenseError("This device has been revoked.", status_code=403, code="device_revoked")

    if session.status != ACTIVE_SESSION_STATUS or session.ended_at is not None:
        raise DesktopLicenseError("Desktop session is not active.", status_code=401, code="inactive_session")

    if device_fingerprint and device.device_fingerprint != device_fingerprint:
        raise DesktopLicenseError("Device fingerprint does not match this session.", status_code=403, code="device_mismatch")

    subscription = active_subscription(db, session.user)
    if not is_subscription_active(subscription):
        raise DesktopLicenseError("Subscription is inactive.", status_code=403, code="inactive_subscription")

    return session


def desktop_heartbeat(
    db: Session,
    *,
    token: str,
    device_fingerprint: str = "",
    app_version: str = "",
) -> dict[str, Any]:
    session = active_session_for_token(db, token, device_fingerprint=device_fingerprint)
    now = utcnow()
    session.last_heartbeat_at = now
    session.device.last_seen_at = now
    if app_version.strip():
        session.device.app_version = app_version.strip()
    db.commit()
    db.refresh(session)
    return {
        "ok": True,
        "last_heartbeat_at": session.last_heartbeat_at.isoformat() if session.last_heartbeat_at else None,
        "device": device_payload(session.device),
        "subscription": subscription_payload(db, session.user),
    }


def desktop_logout(db: Session, *, token: str) -> dict[str, Any]:
    if not token:
        raise DesktopLicenseError("Desktop session token is required.", status_code=401, code="missing_token")

    session = (
        db.query(DesktopSession)
        .filter(DesktopSession.session_token_hash == hash_desktop_token(token))
        .first()
    )
    if session is None:
        return {"ok": True}
    if session.status == ACTIVE_SESSION_STATUS:
        session.status = ENDED_SESSION_STATUS
        session.ended_at = utcnow()
        db.commit()
    return {"ok": True}


def desktop_license(db: Session, *, token: str, device_fingerprint: str = "") -> dict[str, Any]:
    session = active_session_for_token(db, token, device_fingerprint=device_fingerprint)
    return {
        "user": {
            "id": session.user.id,
            "email": session.user.email,
            "full_name": session.user.full_name,
            "role": session.user.role,
        },
        "device": device_payload(session.device),
        "session": {
            "id": session.id,
            "status": session.status,
            "started_at": session.started_at.isoformat(),
            "last_heartbeat_at": session.last_heartbeat_at.isoformat() if session.last_heartbeat_at else None,
        },
        "subscription": subscription_payload(db, session.user),
        "features": plan_features(active_subscription(db, session.user).plan),
        "vault": {
            "storage_mode": session.user.vault_storage_mode,
            "large_documents_upload_by_default": False,
        },
    }


def device_payload(device: DesktopDevice) -> dict[str, Any]:
    return {
        "id": device.id,
        "device_name": device.device_name,
        "device_fingerprint": device.device_fingerprint,
        "platform": device.platform,
        "app_version": device.app_version,
        "status": device.status,
        "last_seen_at": device.last_seen_at.isoformat() if device.last_seen_at else None,
        "created_at": device.created_at.isoformat(),
        "revoked_at": device.revoked_at.isoformat() if device.revoked_at else None,
    }
