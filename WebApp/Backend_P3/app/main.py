from __future__ import annotations

from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse

from app.config import settings
from app.db import Base, engine
from app.routers import admin, health, unity

BASE_DIR = Path(__file__).resolve().parent.parent


@asynccontextmanager
async def lifespan(app: FastAPI):
    if settings.auto_create_db:
        Base.metadata.create_all(bind=engine)
    yield


app = FastAPI(title=settings.app_name, version="1.0.0", lifespan=lifespan)
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origins,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.include_router(health.router)
app.include_router(unity.router)
app.include_router(admin.router)


@app.get("/admin", include_in_schema=False)
def local_admin_console():
    return FileResponse(BASE_DIR / "static" / "admin.html")
