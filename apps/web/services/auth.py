from __future__ import annotations

import base64
import hashlib
import hmac
import secrets
from datetime import datetime, timedelta
from typing import Annotated

from fastapi import Depends, HTTPException, Request
from fastapi.responses import Response
from sqlalchemy.orm import Session

from apps.web.database import get_db
from apps.web.models import AuthSession, User
from apps.web.settings import get_settings


SESSION_COOKIE = "roleaxis_session"
SESSION_DAYS = 14
PBKDF2_ITERATIONS = 390_000


def normalize_email(email: str) -> str:
    return (email or "").strip().lower()


def _b64(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("ascii").rstrip("=")


def _hash_token(token: str) -> str:
    return hashlib.sha256(token.encode("utf-8")).hexdigest()


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    digest = hashlib.pbkdf2_hmac(
        "sha256",
        password.encode("utf-8"),
        salt,
        PBKDF2_ITERATIONS,
    )
    return f"pbkdf2_sha256${PBKDF2_ITERATIONS}${_b64(salt)}${_b64(digest)}"


def verify_password(password: str, stored_hash: str) -> bool:
    try:
        scheme, iterations_raw, salt_raw, digest_raw = stored_hash.split("$", 3)
        if scheme != "pbkdf2_sha256":
            return False
        salt = base64.urlsafe_b64decode(salt_raw + "=" * (-len(salt_raw) % 4))
        expected = base64.urlsafe_b64decode(digest_raw + "=" * (-len(digest_raw) % 4))
        actual = hashlib.pbkdf2_hmac(
            "sha256",
            password.encode("utf-8"),
            salt,
            int(iterations_raw),
        )
    except Exception:
        return False
    return hmac.compare_digest(actual, expected)


def validate_password(password: str) -> str | None:
    if len(password or "") < 8:
        return "Use at least 8 characters for your password."
    if password.lower() == password or password.upper() == password:
        return "Use a mix of uppercase and lowercase letters."
    if not any(char.isdigit() for char in password):
        return "Add at least one number."
    return None


def register_user(
    db: Session,
    *,
    full_name: str,
    company_name: str,
    email: str,
    password: str,
) -> tuple[User | None, str | None]:
    email_normalized = normalize_email(email)
    if not full_name.strip():
        return None, "Enter your full name."
    if "@" not in email_normalized:
        return None, "Enter a valid business email address."
    password_error = validate_password(password)
    if password_error:
        return None, password_error
    existing = db.query(User).filter(User.email == email_normalized).first()
    if existing:
        return None, "An account already exists for that email."

    user = User(
        email=email_normalized,
        full_name=full_name.strip(),
        company_name=company_name.strip(),
        password_hash=hash_password(password),
        role="Owner",
        is_active=True,
    )
    db.add(user)
    db.commit()
    db.refresh(user)
    return user, None


def authenticate_user(db: Session, email: str, password: str) -> User | None:
    user = db.query(User).filter(User.email == normalize_email(email)).first()
    if not user or not user.is_active or not user.password_hash:
        return None
    if not verify_password(password, user.password_hash):
        return None
    user.last_login_at = datetime.utcnow()
    db.commit()
    db.refresh(user)
    return user


def create_session(db: Session, user: User) -> str:
    token = secrets.token_urlsafe(48)
    session = AuthSession(
        user_id=user.id,
        token_hash=_hash_token(token),
        expires_at=datetime.utcnow() + timedelta(days=SESSION_DAYS),
    )
    db.add(session)
    db.commit()
    return token


def set_session_cookie(response: Response, token: str) -> None:
    settings = get_settings()
    response.set_cookie(
        SESSION_COOKIE,
        token,
        max_age=SESSION_DAYS * 24 * 60 * 60,
        httponly=True,
        secure=settings.environment not in {"local", "development", "dev"},
        samesite="lax",
    )


def clear_session_cookie(response: Response) -> None:
    response.delete_cookie(SESSION_COOKIE)


def revoke_session(db: Session, token: str | None) -> None:
    if not token:
        return
    session = db.query(AuthSession).filter(AuthSession.token_hash == _hash_token(token)).first()
    if session and session.revoked_at is None:
        session.revoked_at = datetime.utcnow()
        db.commit()


def get_current_user(
    request: Request,
    db: Annotated[Session, Depends(get_db)],
) -> User | None:
    token = request.cookies.get(SESSION_COOKIE)
    if not token:
        return None

    session = (
        db.query(AuthSession)
        .filter(AuthSession.token_hash == _hash_token(token))
        .first()
    )
    if not session or session.revoked_at is not None or session.expires_at <= datetime.utcnow():
        return None
    if not session.user.is_active:
        return None
    return session.user


def require_user(
    user: Annotated[User | None, Depends(get_current_user)],
) -> User:
    if user is None:
        raise HTTPException(status_code=303, headers={"Location": "/login"})
    return user

