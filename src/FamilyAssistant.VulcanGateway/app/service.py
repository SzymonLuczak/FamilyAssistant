import asyncio
import json
import os
import time
from datetime import datetime, timezone
from pathlib import Path
from uuid import uuid4

from app.registration import read_registration


class Failure(Exception):
    def __init__(self, code, status=503):
        self.code, self.status = code, status


class Service:
    def __init__(self, path, provider):
        self.path = Path(path)
        self.provider = provider
        self.lock = asyncio.Lock()
        self.failures = 0
        self.blocked_until = 0

    def load(self):
        if not self.path.exists():
            return {}
        return json.loads(self.path.read_text(encoding='utf-8'))

    def save(self, data):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temp = self.path.with_name(f'.{uuid4().hex}.tmp')
        try:
            with os.fdopen(os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'w', encoding='utf-8') as stream:
                json.dump(data, stream, ensure_ascii=False)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temp, self.path)
        finally:
            temp.unlink(missing_ok=True)

    def status(self):
        state = self.load()
        return {'connection': 'registered' if state else 'not_connected',
                'students': [{k: v for k, v in s.items() if k not in ('restUrl', 'pupilId')}
                             for s in state.get('students', [])]}

    async def register(self, content):
        try:
            tokens, tenant = read_registration(content)
        except ValueError as ex:
            raise Failure(str(ex), 400) from None
        async with self.lock:
            try:
                # Registration is a write: never retry automatically.
                credential, students = await self.provider.register(tokens, tenant)
            except Exception:
                raise Failure('registration_failed') from None
            self.save({'credential': credential, 'students': students, 'snapshots': {}})
            self.failures, self.blocked_until = 0, 0
            return self.status()

    async def schedule(self, student_id, day):
        async with self.lock:
            state = self.load()
            student = next((s for s in state.get('students', []) if s['id'] == student_id), None)
            if student is None:
                raise Failure('student_not_found', 404)
            key = f'{student_id}:{day.isoformat()}'
            snapshot = state['snapshots'].get(key)
            failed = time.monotonic() < self.blocked_until
            if not failed:
                try:
                    # A total deadline plus at most two read attempts.
                    async with asyncio.timeout(28):
                        for attempt in range(2):
                            try:
                                lessons = await self.provider.schedule(state['credential'], student, day)
                                break
                            except (TimeoutError, OSError):
                                if attempt:
                                    raise
                                await asyncio.sleep(0.25)
                    lessons.sort(key=lambda lesson: (lesson['start'], lesson['id']))
                    uncertain = any(x['status'] == 'change_requires_review' for x in lessons)
                    active = [x for x in lessons if x['status'] == 'scheduled']
                    snapshot = {'studentId': student_id, 'date': day.isoformat(), 'lessons': lessons,
                                'firstLesson': min((x['start'] for x in active), default=None) if not uncertain else None,
                                'lastLesson': max((x['end'] for x in active), default=None) if not uncertain else None,
                                'requiresReview': uncertain, 'timeZone': 'Europe/Warsaw',
                                'fetchedAt': datetime.now(timezone.utc).isoformat()}
                    state['snapshots'][key] = snapshot
                    # Keep at most 120 last successful student/day snapshots.
                    state['snapshots'] = dict(sorted(state['snapshots'].items(), key=lambda p: p[1]['fetchedAt'])[-120:])
                    self.save(state)
                    self.failures = 0
                    return {**snapshot, 'stale': False}
                except Exception:
                    self.failures += 1
                    if self.failures >= 3:
                        self.blocked_until = time.monotonic() + 30
            if snapshot is not None:
                return {**snapshot, 'stale': True, 'error': 'provider_unavailable'}
            raise Failure('provider_unavailable_no_snapshot')
