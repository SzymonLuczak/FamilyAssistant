import os
from datetime import date, timedelta
from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from app.provider import Provider
from app.service import Service, Failure

app = FastAPI(title="FamilyAssistant.VulcanGateway", version="0.1.0")
service = Service(os.getenv('VULCAN_STATE_PATH', '/app/session/state.json'), Provider())


@app.exception_handler(Failure)
async def failure_handler(request, exc):
    return JSONResponse({'error': exc.code}, status_code=exc.status)


@app.exception_handler(RequestValidationError)
async def validation_handler(request, exc):
    # FastAPI's default detail can echo submitted credentials.
    return JSONResponse({'error': 'invalid_request'}, status_code=400)


@app.exception_handler(Exception)
async def safe_error(request, exc):
    return JSONResponse({'error': 'vulcan_operation_failed'}, status_code=503)


@app.get('/status')
def connection():
    return service.status()


@app.post('/register')
async def register(request: Request):
    content = bytearray()
    async for chunk in request.stream():
        content.extend(chunk)
        if len(content) > 1_000_000:
            raise Failure('export_too_large', 413)
    try:
        text = content.decode('utf-8-sig')
    except UnicodeError:
        raise Failure('invalid_export_encoding', 400) from None
    return await service.register(text)


@app.get('/students/{student_id}/schedule/{day}')
async def schedule(student_id: str, day: date):
    from zoneinfo import ZoneInfo
    from datetime import datetime
    today = datetime.now(ZoneInfo('Europe/Warsaw')).date()
    if not today - timedelta(days=30) <= day <= today + timedelta(days=90):
        raise Failure('date_out_of_range', 400)
    return await service.schedule(student_id, day)


@app.get("/health")
def health():
    return {"status": "healthy"}


@app.get("/")
def status():
    return {"service": "FamilyAssistant.VulcanGateway", "milestone": 4,
            "integrations": "read_only"}
