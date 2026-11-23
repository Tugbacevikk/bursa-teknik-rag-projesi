# Two images from one file: "runtime" is the service (what compose builds for the server),
# "test" is the same image with pytest and the tests (PLANNING Testler, T4).
FROM python:3.15-slim AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    # Downloaded models live here; compose mounts a volume so they survive rebuilds.
    HF_HOME=/models

WORKDIR /app

# The default torch wheel bundles GPU libraries (~2 GB); the CPU build is enough here.
# Installed first and on its own so this large layer is rebuilt only when torch changes.
RUN pip install --index-url https://download.pytorch.org/whl/cpu torch==2.15.0

COPY requirements.txt .
RUN pip install -r requirements.txt

COPY app ./app

RUN useradd --create-home --uid 1000 appuser \
    && mkdir -p /models \
    && chown appuser /models
USER appuser

EXPOSE 8000

# 200 only once the model is loaded. The first start downloads it, which can take many
# minutes; failures during start-period do not count against the container.
HEALTHCHECK --interval=15s --timeout=5s --retries=4 --start-period=30m \
    CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/health')"

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]


# The tests run where the service runs: torch and the other packages exactly as in
# production, whatever Python the developer's machine has. Models and Gemini are faked;
# nothing is downloaded. docker compose run --rm ai-service-tests
FROM runtime AS test

USER root
COPY requirements-dev.txt pytest.ini ./
RUN pip install -r requirements-dev.txt
COPY tests ./tests
USER appuser

HEALTHCHECK NONE
CMD ["python", "-m", "pytest"]
