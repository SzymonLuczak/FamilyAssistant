from fastapi.testclient import TestClient
from app.main import app


def test_health_without_credentials():
    with TestClient(app) as client:
        response = client.get("/health")
        assert response.status_code == 200
        assert response.json() == {"status": "healthy"}


def test_read_only_integration_does_not_collect_passwords():
    with TestClient(app) as client:
        assert client.get("/").json()["integrations"] == "read_only"
        assert client.post("/auth/login").status_code == 404
