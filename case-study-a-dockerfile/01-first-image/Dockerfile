# ============================================================================
#  Case study A, example 1 - the simplest possible Dockerfile
#  Deliberately unoptimised. This is the starting point we will improve on.
# ============================================================================

# FROM selects the base image we build on top of.
# This one carries the .NET 10 SDK - the compiler and all build tooling.
FROM mcr.microsoft.com/dotnet/sdk:10.0

# WORKDIR sets the working directory inside the image, creating it if needed.
# Every instruction that follows is relative to it.
WORKDIR /src

# COPY moves files from the build context (your disk) into the image.
# "COPY . ." means "the whole context into the current working directory".
# This example has no ignore file, so the bin/ and obj/ folders produced by
# building on the host come along too - we deal with that in example 2.
COPY . .

# RUN executes a command while the image is being built and stores the result
# as a new layer. dotnet publish compiles the app into /app, ready to run.
RUN dotnet publish src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj -c Release -o /app

# Move into the folder holding the published application.
WORKDIR /app

# EXPOSE is documentation only - it tells readers and tools which port the
# application listens on. It does NOT open the port; "docker run -p" does.
# The number 8080 is not arbitrary: the .NET base images ship with
# ASPNETCORE_HTTP_PORTS=8080, so the app listens there automatically.
EXPOSE 8080

# ENTRYPOINT is the command executed when the container starts.
# The bracket form (exec form) is the correct one: the process runs as PID 1
# and receives the signal sent by "docker stop".
ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]
