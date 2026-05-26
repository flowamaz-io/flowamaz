namespace Flowamaz.Core.Enums;

public enum ConnectorAuthType
{
    ApiKey = 0,
    OAuth2AuthCode = 1,
    OAuth2ClientCredentials = 2,
    Basic = 3,
    Custom = 4,
    ConnectionString = 5,
    HmacSecret = 6,
    None = 7,
    BearerToken = 8,
    ByomEndpoint = 9
}
