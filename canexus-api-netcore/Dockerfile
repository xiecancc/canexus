# canexus-api-netcore 单服务镜像（P1-A5 容器化部署）
# 多阶段构建：SDK 编译 → ASP.NET Core 运行时精简镜像
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 仅复制构建所需文件，最大化层缓存命中（SDK 镜像自带 nuget.org 默认源，
# 仓库未维护私有源配置，故不复制 NuGet.Config，避免构建阶段因缺文件失败）
COPY src/ ./src/
RUN dotnet restore src/API/API.csproj
RUN dotnet publish src/API/API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# 容器内监听端口（docker-compose 中 ASPNETCORE_URLS 亦显式指定，保持一致）
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
# 非 root 运行（ASP.NET Core 官方镜像默认 USER app）
USER $APP_UID
ENTRYPOINT ["dotnet", "API.dll"]
