FROM python:3.12-slim AS base
WORKDIR /app
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
COPY src/FamilyAssistant.VulcanGateway/requirements*.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY src/FamilyAssistant.VulcanGateway/ ./
FROM base AS test
RUN pip install --no-cache-dir -r requirements-dev.txt
ENTRYPOINT ["python", "-m", "pytest", "-q"]
FROM base AS runtime
RUN useradd --create-home --uid 10001 assistant && mkdir -p /app/session && chown assistant:assistant /app/session && chmod 700 /app/session
USER assistant
EXPOSE 8000
HEALTHCHECK --interval=15s --timeout=5s --start-period=15s --retries=3 CMD python -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8000/health', timeout=3)" || exit 1
CMD ["python", "-m", "uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
