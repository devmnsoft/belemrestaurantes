FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj src/BelemRestaurantes.Web/
RUN dotnet restore src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj
COPY src/BelemRestaurantes.Web/ src/BelemRestaurantes.Web/
RUN dotnet publish src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","BelemRestaurantes.Web.dll"]