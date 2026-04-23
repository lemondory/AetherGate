from pydantic import BaseModel

from app.models.enums import AuthProvider, UserRole


class TokenResponse(BaseModel):
    access_token: str
    token_type:   str = "bearer"
    guest_key:    str | None = None  # Guest 최초 발급 시에만 포함


class GuestLoginRequest(BaseModel):
    guest_key: str


class UserProfile(BaseModel):
    id:       str
    provider: AuthProvider
    role:     UserRole
    username: str | None
