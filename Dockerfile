# Multi-stage build for the Emplyo web application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Emplyo.sln ./
COPY src/Emplyo.Domain/Emplyo.Domain.csproj src/Emplyo.Domain/
COPY src/Emplyo.Application/Emplyo.Application.csproj src/Emplyo.Application/
COPY src/Emplyo.Infrastructure/Emplyo.Infrastructure.csproj src/Emplyo.Infrastructure/
COPY src/Emplyo.Web/Emplyo.Web.csproj src/Emplyo.Web/
RUN dotnet restore src/Emplyo.Web/Emplyo.Web.csproj

COPY . .
RUN dotnet publish src/Emplyo.Web/Emplyo.Web.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Emplyo.Web.dll"]
