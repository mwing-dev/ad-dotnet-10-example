# dotnet-linux-ad-kerberos

This repository is not a complete authentication system, an identity provider, or a reusable library.

It is a minimal program whose entire purpose is to answer one question:

> Can a Linux Docker container running .NET actually authenticate to Active Directory using Kerberos and perform an LDAP query?

If you routinely build .NET + LDAP integrations and can identify a broken SPN from the error message alone, this probably won't add much to your life.

If you're here because you've spent the evening generating increasingly creative ways to **not** bind to LDAP; possibly assisted by a generous consumption of Copilot tokens; this exists to give you a known-good baseline so you can stop guessing and start narrowing things down.

Nothing here is revolutionary. The value is simply that these exact pieces worked together at the same time, without disabling domain security, downgrading policies, or sacrificing livestock to the Kerberos gods.

---

## What This Does

Inside a Linux container, the application:

1. Uses a keytab to obtain a Kerberos ticket
2. Performs an LDAP bind using `AuthType.Negotiate`
3. Queries RootDSE to discover the domain naming context
4. Retrieves a random user object from Active Directory

If a user is printed, authentication worked correctly.

If it fails, the failure is environmental; DNS, SPN, Kerberos configuration, or time synchronization, not application logic.

---

## What This Is Not

This repository intentionally does **not** attempt to demonstrate:

* Password authentication
* ASP.NET Identity integration
* LDAPS certificate validation strategies
* Domain joining a container
* Production credential management

Those problems only become worth solving after basic connectivity is proven.
This project answers the earlier question: *Does the plumbing even work?*

---

## Expected Output

Successful run:

```
Starting LDAP Kerberos test...
Binding using Kerberos ticket...
SUCCESS - Kerberos LDAP bind worked!
Random AD user: CN=Jane Doe, sAMAccountName=jdoe, UPN=jdoe@example-ad.local
```

If you instead get `InvalidCredentials`, the credentials are usually fine; the container never actually authenticated.
Active Directory just isn't in the habit of explaining Kerberos failure modes politely.

---

## Repository Layout

```
Program.cs              Performs bind and query
Dockerfile              Installs Kerberos + LDAP dependencies
docker-entrypoint.sh    Acquires Kerberos ticket before execution
of docker-compose.yml. Provides DNS configuration and mounts the keytab

ad-files/
    krb5.conf           Kerberos configuration (edit for your domain)
    ldap.conf           LDAP client configuration
    service.keytab      Placeholder; you supply a real one
```

No real credentials are included in this repository.
If you commit a real keytab, it will technically work, but it will also ruin your week later.

---

## Requirements

You need:

* A reachable domain controller
* A service account
* An SPN mapped to that account
* A generated keytab

You do not need to join the container to the domain.

---

## Setup

### 1 Create a Service Account

Create a regular domain user.
Read access to the Directory is sufficient.

If the process seems to require Domain Admin, something else is wrong.

---

### 2 Register SPN

Run on a domain controller:

```
setspn -S HTTP/apphost.example-ad.local svc_dotnet_test
```

Kerberos cares deeply about exact spelling.
Close enough is indistinguishable from completely incorrect.

---

### 3 Generate Keytab

```
ktpass -princ HTTP/apphost.example-ad.local@EXAMPLE-AD.LOCAL -mapuser svc_dotnet_test -crypto AES256-SHA1 -ptype KRB5_NT_PRINCIPAL -pass * -out service.keytab
```

Place the generated file into:

```
ad-files/service.keytab
```

---

### 4 Configure Domain Values

Edit `ad-files/krb5.conf` and `docker-compose.yml`:

* Realm
* Domain controller hostname
* DNS server
* Kerberos principal

All of them must match reality exactly, including the case.
Kerberos does not interpret intent.

---

### 5 Run

```
docker compose build
docker compose up
```

---

## Interpreting Failures

Active Directory errors are usually correct but rarely helpful.

| Error                    | Usually Means                           |
| ------------------------ | --------------------------------------- |
| `InvalidCredentials`     | No Kerberos ticket was used             |
| `Server not operational` | DNS or routing failure                  |
| `KDC unreachable`        | Time sync or realm mismatch             |
| Works on Windows only    | Windows obtained a ticket automatically |

If the bind fails, assume infrastructure first and code last.
LDAP is typically the messenger, not the culprit.

---

## Why This Exists

When debugging AD integrations, multiple subsystems fail simultaneously:

* DNS resolution
* Kerberos configuration
* SPN mapping
* Clock synchronization
* LDAP negotiation

Trying to debug all of them at once produces extremely confident but incorrect conclusions.
This project reduces the problem to a single yes/no test so you can move forward without guessing.

---

## Security Notes

The repository only includes placeholder configuration files.

You must supply your own:

* Keytab
* Domain information
* DNS configuration

Keytabs effectively act as non-expiring credentials.
Treat them with the same care you would a password; just one that never politely rotates.

---

## Final Notes

Active Directory is very consistent once configured correctly, but it is strict about names, time, and DNS. Minor mismatches tend to lead to significant, misleading failures.

This repository documents one configuration known to work.
If this succeeds, your remaining bugs will be normal bugs again, which are significantly easier to reason about than Kerberos bugs.

---

## Appendix The Things That Looked Like The Problem (But Weren't)

What follows is the condensed version of several hours of confidently debugging the wrong layer.

Almost every failure appeared to be an application issue.
Almost none of them were.

---

### 1) Suspecting the .NET LDAP Code

The first assumption was that the bind was failing because the application was incorrect.

So we tried most of the knobs exposed by `System.DirectoryServices.Protocols`:

* Basic credentials via `NetworkCredential`
* Explicit username/password bind
* Different `AuthType` values
* Setting Signing / Sealing
* StartTLS
* LDAPS (636)
* Certificate validation callbacks
* Explicit domain values
* Explicit SASL configuration
* `SecureSocketLayer`
* Different bind overloads
* Exporting `KRB5CCNAME`
* Running as root vs non-root

The errors were consistent but unhelpful:

```
LdapException: A local error occurred
The feature is not supported
LDAP server unavailable
Unknown authentication error
```

**Reality**

.NET was behaving correctly the entire time; it simply had no usable Kerberos identity to present.

The bind wasn't wrong.
The client identity didn't exist.

---

### 2) Suspecting Docker / Linux

Containers felt suspicious, so we removed them from the equation.

Tried:

* Running outside Docker
* Different base images
* Different users
* Mounting `/tmp`
* Keytab permissions
* Environment variables

Nothing changed.

**Reality**

Kerberos was already failing before .NET was involved.
Docker just made the failure more obvious.

---

### 3) Suspecting TLS

At this point, encryption became the prime suspect.

We tried:

* Installing CA certificates
* Trusting the domain CA
* Custom validation callbacks
* Forcing LDAPS repeatedly
* Switching between LDAP and LDAPS

**Reality**

Kerberos SASL over LDAP does not require LDAPS.
TLS had nothing to do with the failure.

---

### 4) Suspecting the Keytab

Several perfectly reasonable-looking keytabs were generated.

They still didn't work.

Problems encountered:

* Wrong principal format
* Missing SPN
* KVNO assumptions
* Crypto mismatches
* Using a user UPN instead of a service principal

Errors included:

```
Server not found in Kerberos database.
Matching credentials not found
Pre-authentication failed
```

All believable.
All misleading.

---

### 5) The Hostname Canonicalization Problem

This turned out to be the actual blocker.

Kerberos kept requesting tickets for:

```
ldap/2a7c:91b4:4d2e:7f03:8c9a:1d55:ae42:6b10
```

instead of:

```
ldap://dc1.example-ad.local
```

So the ticket was technically valid…
just not for the service we were trying to use.

Symptoms looked contradictory:

```
kinit works
kvno works
ldapwhoami fails
.NET fails
```

**Reality**

Reverse lookup and hostname canonicalization changed the requested SPN.

The system successfully authenticated, but it was the wrong service.

The fix:

```
SASL_NOCANON on
rdns = false
dns_canonicalize_hostname = false
```

This was the turning point.

---

### 6) Almost Blaming Active Directory Security

At various points, the temptation existed to "just make AD less strict".

Considered:

* disabling LDAP signing
* insecure binds
* lowering domain hardening

**Reality**

The domain was correct.
The client's identity was not.

---

## The Real Root Cause

Not code
Not TLS
Not Docker
Not permissions
Not .NET

**Kerberos service principal mismatch caused by hostname canonicalization**

The OS kept requesting a ticket for an identity different from the one the keytab contained.

Once this worked:

```
ldapwhoami -Y GSSAPI -H ldap://dc1.example-ad.local
```

.NET worked immediately afterward.

---

## The Key Insight

We were debugging the application layer while authentication failed at the operating system layer.

Or put another way:

> I wasn't failing authentication; I was successfully authenticating as the wrong service.

Yes — that’s actually a really good tip, and it doesn’t look like a hack.
It reads as: *“prove the infrastructure first, then containerize it.”*
Which is exactly how most people eventually solve Kerberos anyway.

More importantly, it prevents the worst debugging trap:

> Trying to debug Kerberos, DNS, Docker networking, and application code at the same time.

You’re basically giving them a way to collapse the problem into two phases:

1. Does Linux Kerberos work?
2. Does my container mirror Linux?

That’s valuable and realistic.

---

Here’s a version that fits your README tone:

---

## If all else fails

If you’ve replaced the values correctly and still can’t get a ticket, stop debugging the container for a moment and verify the environment first.

Spin up a temporary **Ubuntu 24.04 VM** on the same network as your domain and install the same tools:

```
sudo apt update
sudo apt install krb5-user ldap-utils
```

Then copy your generated `krb5.conf` and test directly on the OS:

```
kinit -k -t service.keytab HTTP/your-host.your-domain@YOUR.DOMAIN
ldapwhoami -Y GSSAPI -H ldap://your-dc.your-domain
```

If this fails, the issue is infrastructure (DNS, SPN, time, or keytab), not Docker or .NET.

If it succeeds, copy the working configuration files into this repository and run the container again.
At that point the container should behave identically.

In short: get Kerberos working on Linux first, then make Docker match Linux.

