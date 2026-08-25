# -- Frontend --

FROM node:22-alpine AS client

WORKDIR /src
COPY NOVAxis/Web/ClientApp/package.json NOVAxis/Web/ClientApp/package-lock.json ./
RUN npm ci

COPY NOVAxis/Web/ClientApp/ ./
RUN npm run build

# -- Build --

FROM mcr.microsoft.com/dotnet/sdk:9.0-noble AS build

ARG LIBDAVE_RELEASE=v1.1.1/cpp
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libopus0 \
        libsodium23 \
        curl \
        unzip \
    && mkdir /natives \
    && cp /usr/lib/*/libopus.so.0 /natives/libopus \
    && cp /usr/lib/*/libsodium.so.23 /natives/libsodium \
    && curl -fsSL -o /tmp/libdave.zip \
        "https://github.com/discord/libdave/releases/download/${LIBDAVE_RELEASE}/libdave-Linux-X64-boringssl.zip" \
    && unzip -j /tmp/libdave.zip lib/libdave.so -d /natives \
    && rm /tmp/libdave.zip \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY . .

# The built frontend publishes along as static content
COPY --from=client /src/dist /app/NOVAxis/wwwroot

WORKDIR /app/NOVAxis
RUN dotnet restore
RUN dotnet publish -c Release -o out

# -- Runtime --

FROM mcr.microsoft.com/dotnet/runtime:9.0-noble

# ffmpeg and yt-dlp back the in-process audio streaming
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ffmpeg \
        python3 \
        python3-venv \
    && python3 -m venv /opt/yt-dlp \
    && /opt/yt-dlp/bin/pip install --no-cache-dir --upgrade yt-dlp \
    && ln -s /opt/yt-dlp/bin/yt-dlp /usr/local/bin/yt-dlp \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/NOVAxis/out .
COPY --from=build /natives/libopus /natives/libsodium /natives/libdave.so ./

# The web player
EXPOSE 5000

ENTRYPOINT ["/app/NOVAxis"]
