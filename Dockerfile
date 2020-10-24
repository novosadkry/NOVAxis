# -- Build --

FROM mcr.microsoft.com/dotnet/core/sdk:2.1 AS build
WORKDIR /app

COPY . .

WORKDIR /app/NOVAxis
RUN dotnet publish -c Release -r alpine-x64 -p:PublishTrimmed=true -o out --self-contained true

# -- Runtime -- 

FROM alpine:latest

# Libraries required by .NET
RUN apk add --no-cache libstdc++ libintl icu

WORKDIR /app
COPY --from=build /app/NOVAxis/out .
ENTRYPOINT ["/app/NOVAxis"]