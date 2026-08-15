FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG PROJECT
RUN dotnet publish "$PROJECT" -c Release -o /app/publish --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN useradd --create-home --uid 10001 appuser
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
USER 10001
ARG ASSEMBLY
ENV ASSEMBLY=${ASSEMBLY}
ENTRYPOINT ["sh", "-c", "dotnet \"$ASSEMBLY\""]
