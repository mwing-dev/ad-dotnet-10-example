using System.DirectoryServices.Protocols;

Console.WriteLine("Starting LDAP Kerberos test...");

var ldapHost = Environment.GetEnvironmentVariable("AD_LDAP_HOST") ?? "CHANGE_THIS_TO_YOUR_AD_LDAP_HOST";
var identifier = new LdapDirectoryIdentifier(ldapHost, 389);
using var connection = new LdapConnection(identifier);

connection.AuthType = AuthType.Negotiate;
connection.SessionOptions.ProtocolVersion = 3;
connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

Console.WriteLine("Binding using Kerberos ticket...");
connection.Bind();

Console.WriteLine("SUCCESS — Kerberos LDAP bind worked!");

var rootDseRequest = new SearchRequest(
	string.Empty,
	"(objectClass=*)",
	SearchScope.Base,
	"defaultNamingContext");

var rootDseResponse = (SearchResponse)connection.SendRequest(rootDseRequest);
if (rootDseResponse.Entries.Count == 0 || !rootDseResponse.Entries[0].Attributes.Contains("defaultNamingContext"))
{
	Console.WriteLine("Could not determine AD default naming context.");
	return;
}

var defaultNamingContext = rootDseResponse.Entries[0].Attributes["defaultNamingContext"]?[0]?.ToString();
if (string.IsNullOrWhiteSpace(defaultNamingContext))
{
	Console.WriteLine("Default naming context was empty.");
	return;
}

var userSearchRequest = new SearchRequest(
	defaultNamingContext,
	"(&(objectCategory=person)(objectClass=user)(!(objectClass=computer)))",
	SearchScope.Subtree,
	"sAMAccountName",
	"userPrincipalName",
	"cn");

userSearchRequest.SizeLimit = 500;

var userSearchResponse = (SearchResponse)connection.SendRequest(userSearchRequest);
if (userSearchResponse.Entries.Count == 0)
{
	Console.WriteLine("No AD user objects were returned.");
	return;
}

var randomIndex = Random.Shared.Next(userSearchResponse.Entries.Count);
var randomUser = userSearchResponse.Entries[randomIndex];

string? GetAttributeValue(SearchResultEntry entry, string attributeName)
{
	if (!entry.Attributes.Contains(attributeName))
	{
		return null;
	}

	return entry.Attributes[attributeName]?[0]?.ToString();
}

var samAccountName = GetAttributeValue(randomUser, "sAMAccountName") ?? "(no sAMAccountName)";
var userPrincipalName = GetAttributeValue(randomUser, "userPrincipalName") ?? "(no userPrincipalName)";
var commonName = GetAttributeValue(randomUser, "cn") ?? "(no cn)";

Console.WriteLine($"Random AD user: CN={commonName}, sAMAccountName={samAccountName}, UPN={userPrincipalName}");
