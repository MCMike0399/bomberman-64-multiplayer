# Build from the allowlisted portable runtime produced by tools/ci/package_server.py.
# The build context contains no kernel source or host configuration.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --chown=app:app server/ ./
RUN chown app:app /app
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bomber.Server.dll", "--urls", "http://0.0.0.0:8080"]
