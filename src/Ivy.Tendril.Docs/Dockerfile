FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
ARG IVY_FRAMEWORK_REF=main
WORKDIR /src

RUN apt-get update && apt-get install -y git curl && rm -rf /var/lib/apt/lists/*

# Install Node.js + pnpm + vite-plus (Ivy.csproj MSBuild targets run "vp install/build")
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    npm install -g pnpm@10.33.0 vite-plus@0.1.20

RUN git clone --depth 1 --branch $IVY_FRAMEWORK_REF \
    https://github.com/Ivy-Interactive/Ivy-Framework.git /src/Ivy-Framework

COPY . /src/Ivy-Tendril

# Install ivy-docs-cli for markdown transformation (TransformMarkdown MSBuild target).
RUN mkdir -p /src/Ivy-Tendril/.config && \
    echo '{"version":1,"isRoot":true,"tools":{"ivy.docs.compiler":{"version":"0.1.0","commands":["ivy-docs-cli"]}}}' \
    > /src/Ivy-Tendril/.config/dotnet-tools.json && \
    dotnet tool install --global Ivy.Docs.Compiler || true
ENV PATH="$PATH:/root/.dotnet/tools"

RUN --mount=type=cache,id=nuget-tendril-docs,target=/root/.nuget/packages \
    dotnet publish "/src/Ivy-Tendril/src/Ivy.Tendril.Docs/Ivy.Tendril.Docs.csproj" \
    -a ${TARGETARCH:-x64} \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:PublishReadyToRun=true \
    /p:IvySource=true

FROM base AS final
WORKDIR /app
USER root
RUN apt-get update && apt-get install -y --no-install-recommends \
    libc6 \
    libgcc-s1 \
    && apt-get clean && rm -rf /var/lib/apt/lists/*
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ivy.Tendril.Docs.dll"]
