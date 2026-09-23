import asyncio
import base64
import html
import json
import time
from datetime import date, time as clock
from types import SimpleNamespace as Obj

import pytest
from fastapi.testclient import TestClient
from app.registration import read_registration
from app.provider import Provider, normalize, safe_rest_url
from app.service import Service, Failure


def export(tenant='school', expiry=None):
    claims = json.dumps({'tenant': tenant, 'exp': expiry or time.time() + 300}).encode()
    token = 'header.' + base64.urlsafe_b64encode(claims).decode().rstrip('=') + '.signature'
    return json.dumps({'Tokens': [token], 'Success': True, 'IsConsentAccepted': True})


def test_import_saved_html_and_json_without_storing_access_token():
    data = export()
    assert read_registration(data) == read_registration('<input type="hidden" value="' + html.escape(data, quote=True) + '">')


@pytest.mark.parametrize('content', [export('../elsewhere'), export(expiry=1), '{"Tokens":[]}', '<html>login form</html>'])
def test_invalid_registration_is_rejected(content):
    with pytest.raises(ValueError):
        read_registration(content)


@pytest.mark.parametrize('url', ['http://lekcjaplus.vulcan.net.pl/api', 'https://example.com/api',
                                 'https://lekcjaplus.vulcan.net.pl.evil.test/api', 'https://user@lekcjaplus.vulcan.net.pl/api'])
def test_provider_urls_are_restricted(url):
    with pytest.raises(ValueError):
        safe_rest_url(url)


def lesson(sub=None):
    return Obj(id=7, time_slot=Obj(start=clock(8), end=clock(8, 45)), substitution=sub,
               subject=Obj(name='Matematyka'), event=None, date_=date(2026, 9, 22))


def test_changes_are_not_guessed_as_confirmed_lessons():
    day = date(2026, 9, 22)
    assert normalize(lesson(), day, False)['status'] == 'scheduled'
    change = Obj(class_absence=False, pupil_note=None, reason='Zmiana')
    assert normalize(lesson(change), day, False)['status'] == 'change_requires_review'
    change.class_absence = True
    assert normalize(lesson(change), day, False)['status'] == 'cancelled'


class FakeProvider:
    broken = False
    calls = 0
    async def register(self, tokens, tenant):
        if self.broken:
            raise RuntimeError('secret from provider')
        return {'private_key': 'test-secret'}, [{'id': 'a' * 24, 'name': 'Uczeń', 'pupilId': 1, 'restUrl': 'private'}]

    async def schedule(self, credential, student, day):
        self.calls += 1
        if self.broken:
            raise RuntimeError('secret from provider')
        return [normalize(lesson(), day, False)]


def test_snapshot_survives_restart_and_does_not_cross_dates_or_students(tmp_path):
    async def scenario():
        provider = FakeProvider()
        path = tmp_path / 'state.json'
        service = Service(path, provider)
        await service.register(export())
        fresh = await service.schedule('a' * 24, date(2026, 9, 22))
        assert fresh['stale'] is False and fresh['lastLesson'] == '08:45:00'
        provider.broken = True
        restarted = Service(path, provider)
        cached = await restarted.schedule('a' * 24, date(2026, 9, 22))
        assert cached['stale'] is True and cached['fetchedAt'] == fresh['fetchedAt']
        with pytest.raises(Failure, match='') as error:
            await restarted.schedule('a' * 24, date(2026, 9, 23))
        assert error.value.code == 'provider_unavailable_no_snapshot'
        with pytest.raises(Failure) as error:
            await restarted.schedule('b' * 24, date(2026, 9, 22))
        assert error.value.status == 404
        assert 'private_key' not in json.dumps(service.status())
        assert 'restUrl' not in json.dumps(service.status())
    asyncio.run(scenario())


def test_failed_registration_keeps_account_and_success_clears_snapshots(tmp_path):
    async def scenario():
        provider = FakeProvider()
        service = Service(tmp_path / 'state.json', provider)
        await service.register(export())
        await service.schedule('a' * 24, date(2026, 9, 22))
        before = service.load()
        provider.broken = True
        with pytest.raises(Failure) as error:
            await service.register(export())
        assert error.value.code == 'registration_failed'
        assert service.load() == before
        provider.broken = False
        await service.register(export())
        assert service.load()['snapshots'] == {}
    asyncio.run(scenario())


def test_circuit_breaker_stops_repeated_requests(tmp_path):
    async def scenario():
        provider = FakeProvider()
        service = Service(tmp_path / 'state.json', provider)
        await service.register(export())
        provider.broken = True
        for _ in range(4):
            with pytest.raises(Failure):
                await service.schedule('a' * 24, date(2026, 9, 22))
        assert provider.calls == 3
    asyncio.run(scenario())


def test_pagination_rejects_repeated_full_page():
    async def get(**kwargs):
        return [Obj(id=i) for i in range(100)]
    with pytest.raises(ValueError, match='invalid_pagination'):
        asyncio.run(Provider.pages(get, 'url', 1, date.today()))


def test_http_errors_do_not_echo_export(monkeypatch, tmp_path):
    import app.main as main
    monkeypatch.setattr(main, 'service', Service(tmp_path / 'state.json', FakeProvider()))
    with TestClient(main.app) as client:
        response = client.post('/register', content='secret-do-not-echo')
        assert response.status_code == 400
        assert 'secret-do-not-echo' not in response.text
        assert client.get('/status').json() == {'connection': 'not_connected', 'students': []}
        assert client.post('/register', content='x' * 1_000_001).status_code == 413
