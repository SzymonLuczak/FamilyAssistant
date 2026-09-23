import asyncio
from urllib.parse import urlsplit

from iris.api import IrisHebeCeApi
from iris.credentials import RsaCredential


def safe_rest_url(url):
    parsed = urlsplit(url)
    if (parsed.scheme != 'https' or parsed.hostname != 'lekcjaplus.vulcan.net.pl'
            or parsed.username or parsed.password or parsed.port not in (None, 443)
            or parsed.query or parsed.fragment):
        raise ValueError('unsupported_provider_host')
    return url


class Provider:
    async def register(self, tokens, tenant):
        credential = RsaCredential.create_new('Android', 'Family Assistant Dell')
        api = IrisHebeCeApi(credential)
        try:
            async with asyncio.timeout(25):
                await api.register_by_jwt(tokens=tokens, tenant=tenant)
                accounts = await api.get_accounts()
                return credential.model_dump(mode='json'), self.accounts(accounts)
        finally:
            # Pinned Iris release has no working public close method.
            await api._http._client.close()

    @staticmethod
    def accounts(accounts):
        result = []
        for account in accounts:
            url = safe_rest_url(account.unit.rest_url)
            import hashlib
            key = hashlib.sha256(f'{url}|{account.unit.id}|{account.pupil.id}'.encode()).hexdigest()[:24]
            if any(item['id'] == key for item in result):
                continue
            result.append({'id': key, 'name': f'{account.pupil.first_name} {account.pupil.surname}',
                           'school': account.unit.name, 'class': account.class_display,
                           'pupilId': account.pupil.id, 'restUrl': url})
        if not result:
            raise ValueError('no_students')
        return result

    async def schedule(self, credential, student, day):
        api = IrisHebeCeApi(RsaCredential.model_validate(credential))
        try:
            async with asyncio.timeout(25):
                url = safe_rest_url(student['restUrl'])
                lessons = await self.pages(api.get_schedule, url, student['pupilId'], day)
                extra = await self.pages(api.get_schedule_extra, url, student['pupilId'], day)
                return [normalize(item, day, False) for item in lessons] + [normalize(item, day, True) for item in extra]
        finally:
            await api._http._client.close()

    @staticmethod
    async def pages(method, url, pupil, day):
        items, seen, cursor = [], set(), -2147483648
        for _ in range(20):
            page = await method(rest_url=url, pupil_id=pupil, date_from=day, date_to=day,
                                last_id=cursor, page_size=100)
            if not page:
                return items
            ids = {item.id for item in page}
            if ids & seen or len(ids) != len(page):
                raise ValueError('invalid_pagination')
            items.extend(page)
            seen.update(ids)
            if len(page) < 100:
                return items
            cursor = max(ids)
        raise ValueError('pagination_limit')


def normalize(item, day, extra):
    slot = item.time_slot
    sub = item.substitution
    changed = sub is not None
    cancelled = bool(sub and sub.class_absence)
    # Unknown change codes are never interpreted as a confirmed pickup time.
    status = 'cancelled' if cancelled else 'change_requires_review' if changed else 'scheduled'
    if extra:
        subject = item.extra_description or item.schedule_description or 'Zajęcia dodatkowe'
        actual_day = item.day
    else:
        subject = item.subject.name if item.subject else item.event or 'Lekcja'
        actual_day = item.date_
    if actual_day != day:
        raise ValueError('unexpected_schedule_date')
    return {'id': ('extra:' if extra else 'lesson:') + str(item.id), 'subject': subject,
            'start': slot.start.isoformat(), 'end': slot.end.isoformat(), 'status': status,
            'note': (sub.pupil_note or sub.reason or '') if sub else '', 'extra': extra}
