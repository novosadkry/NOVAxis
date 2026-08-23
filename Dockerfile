# -- Build --

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /app
COPY . .

WORKDIR /app/NOVAxis
RUN dotnet restore
RUN dotnet publish -c Release -o out

# -- Runtime --

FROM mcr.microsoft.com/dotnet/runtime:9.0

# libopus and libsodium are used by Discord.Net to encode and encrypt voice traffic,
# ffmpeg and yt-dlp back the in-process audio streaming
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libopus0 \
        libsodium23 \
        ffmpeg \
        python3 \
        python3-venv \
    && python3 -m venv /opt/yt-dlp \
    && /opt/yt-dlp/bin/pip install --no-cache-dir --upgrade yt-dlp \
    && ln -s /opt/yt-dlp/bin/yt-dlp /usr/local/bin/yt-dlp \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/NOVAxis/out .

# Discord.Net resolves those by their unversioned names from the application directory
RUN cp /usr/lib/*/libopus.so.0 /app/libopus \
    && cp /usr/lib/*/libsodium.so.23 /app/libsodium

ENTRYPOINT ["/app/NOVAxis"]
