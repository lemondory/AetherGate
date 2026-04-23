import uuid
from sqlalchemy import Column, String, DateTime, Enum as SAEnum, UniqueConstraint
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.sql import func

from app.core.database import Base
from app.models.enums import AuthProvider, UserRole


class User(Base):
    __tablename__ = "users"

    id          = Column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    provider    = Column(
        SAEnum(AuthProvider, name="auth_provider", create_constraint=True),
        nullable=False,
    )
    provider_id = Column(String, nullable=False)
    username    = Column(String, nullable=True)
    role        = Column(
        SAEnum(UserRole, name="user_role", create_constraint=True),
        nullable=False,
        default=UserRole.USER,
    )
    created_at  = Column(DateTime(timezone=True), server_default=func.now())

    __table_args__ = (
        UniqueConstraint("provider", "provider_id", name="uq_users_provider"),
    )
