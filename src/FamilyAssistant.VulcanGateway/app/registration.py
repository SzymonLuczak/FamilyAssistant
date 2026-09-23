"""Read a user-supplied eduVULCAN registration export; never collect passwords."""
import base64
import json
import re
import time
from html.parser import HTMLParser


class ExportParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.values = []

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == 'input' and values.get('type', '').lower() == 'hidden':
            self.values.append(values.get('value', ''))


def read_registration(content: str):
    if len(content.encode('utf-8')) > 1_000_000:
        raise ValueError('export_too_large')
    candidates = [content.lstrip('\ufeff')]
    parser = ExportParser()
    parser.feed(content)
    candidates.extend(parser.values)
    for candidate in candidates:
        try:
            data = json.loads(candidate)
        except (ValueError, TypeError):
            continue
        if not isinstance(data, dict) or 'Tokens' not in data:
            continue
        if data.get('Success') is not True or data.get('IsConsentAccepted') is not True:
            raise ValueError('consent_required')
        tokens = data['Tokens']
        if not isinstance(tokens, list) or not 1 <= len(tokens) <= 20:
            raise ValueError('no_student_tokens')
        tenants = set()
        for token in tokens:
            try:
                if not isinstance(token, str) or len(token) > 32000 or len(token.split('.')) != 3:
                    raise ValueError()
                encoded = token.split('.')[1]
                claims = json.loads(base64.urlsafe_b64decode(encoded + '=' * (-len(encoded) % 4)))
                tenant = claims['tenant']
                if not isinstance(tenant, str) or not re.fullmatch(r'[A-Za-z0-9_-]{1,80}', tenant):
                    raise ValueError()
                if float(claims['exp']) <= time.time():
                    raise ValueError('expired_export')
                tenants.add(tenant)
            except (ValueError, TypeError, KeyError, UnicodeError):
                raise ValueError('invalid_or_expired_export') from None
        # Decoding only selects a bounded route. Upstream verifies JWT signatures.
        if len(tenants) != 1:
            raise ValueError('multiple_tenants_not_supported')
        return tokens, tenants.pop()
    raise ValueError('registration_export_not_found')
