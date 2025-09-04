# -- Build --

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /app
COPY . .

WORKDIR /app/NOVAxis
RUN dotnet restore
RUN dotnet publish -c Release -o out

# -- Runtime --

FROM mcr.microsoft.com/dotnet/runtime:9.0
RUN apt-get update && apt-get install -y libopus0

WORKDIR /app
COPY --from=build /app/NOVAxis/out .

RUN cp /usr/lib/x86_64-linux-gnu/libopus.so.0 /app/libopus

ENTRYPOINT ["/app/NOVAxis"]
