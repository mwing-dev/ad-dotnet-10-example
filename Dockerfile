FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build

WORKDIR /src
COPY ad-dotnet-10-example.csproj ./
RUN dotnet restore

COPY . ./
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0-preview

# Install kerberos + ldap tools
RUN apt-get update && apt-get install -y \
    krb5-user \
    libsasl2-modules-gssapi-mit \
    ldap-utils \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# copy app
COPY --from=build /out/ .

RUN mkdir -p /etc/security/keytabs

# copy configs into correct system locations
COPY ad-files/krb5.conf /etc/krb5.conf
COPY ad-files/ldap.conf /etc/ldap/ldap.conf

# entrypoint handles authentication
COPY docker-entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
