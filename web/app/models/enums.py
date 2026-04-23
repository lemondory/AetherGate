import enum


class AuthProvider(str, enum.Enum):
    """인증 제공자 — DB에 PostgreSQL native enum으로 저장"""
    GUEST  = "guest"
    GOOGLE = "google"
    KAKAO  = "kakao"


class UserRole(str, enum.Enum):
    """사용자 역할 — JWT role 클레임 및 Admin 접근 제어에 사용"""
    USER  = "user"
    ADMIN = "admin"
