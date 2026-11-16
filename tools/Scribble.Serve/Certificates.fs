/// The HTTPS certificates. WebGPU needs a secure context, so another device on the network must
/// reach the samples over HTTPS with a certificate it accepts.
///
/// By default the tool keeps a small local certificate authority (made once, kept in the
/// certificate folder) and signs a fresh server certificate with it on every run, naming this
/// machine and its current network addresses. Install the authority's certificate once on each
/// device (`scribble-dev-ca.crt`, also offered over plain HTTP) and the samples open without
/// warnings. With `--self-signed`, the server certificate signs itself instead: nothing to install,
/// but every device shows a warning to click through.
module Scribble.Serve.Certificates

open System
open System.IO
open System.Net
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates

let private caName = "CN=Scribble development CA (this machine only)"

/// Reloaded through PKCS#12: on Windows, TLS can't use a certificate whose key only lives in memory.
let private withUsableKey (cert: X509Certificate2) =
    X509CertificateLoader.LoadPkcs12(cert.Export X509ContentType.Pfx, null, X509KeyStorageFlags.Exportable)

/// The machine's names and addresses the server certificate must cover.
let private subjectNames (host: string) (addresses: IPAddress list) =
    let names = SubjectAlternativeNameBuilder()
    for dns in List.distinct [ host; host + ".local"; "localhost" ] do
        names.AddDnsName dns
    for ip in IPAddress.Loopback :: addresses do
        names.AddIpAddress ip
    names.Build()

/// The certificate authority: loaded from `folder`, or made (valid ten years) and saved there as
/// `scribble-dev-ca.pfx` (with its key) and `scribble-dev-ca.crt` (to install on devices).
let private authority (folder: string) =
    let pfx = Path.Combine(folder, "scribble-dev-ca.pfx")
    if File.Exists pfx then
        X509CertificateLoader.LoadPkcs12FromFile(pfx, null, X509KeyStorageFlags.Exportable)
    else
        use key = RSA.Create 2048
        let request = CertificateRequest(caName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
        request.CertificateExtensions.Add(X509BasicConstraintsExtension(true, true, 0, true))
        request.CertificateExtensions.Add(X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign ||| X509KeyUsageFlags.CrlSign, true))
        request.CertificateExtensions.Add(X509SubjectKeyIdentifierExtension(request.PublicKey, false))
        let now = DateTimeOffset.UtcNow
        use ca = request.CreateSelfSigned(now.AddDays -1.0, now.AddYears 10)
        Directory.CreateDirectory folder |> ignore
        File.WriteAllBytes(pfx, ca.Export X509ContentType.Pfx)
        File.WriteAllText(Path.Combine(folder, "scribble-dev-ca.crt"), ca.ExportCertificatePem())
        withUsableKey ca

/// The authority's certificate as PEM text (what devices install), made if needed.
let authorityPem (folder: string) =
    use ca = authority folder
    ca.ExportCertificatePem()

/// A server certificate for this machine (its name, `.local` name, localhost and `addresses`),
/// valid 397 days (the longest Apple devices accept): signed by the local authority, or by itself
/// when `selfSigned`.
let server (folder: string) (selfSigned: bool) (host: string) (addresses: IPAddress list) : X509Certificate2 =
    use key = RSA.Create 2048
    let request = CertificateRequest("CN=" + host, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
    request.CertificateExtensions.Add(X509BasicConstraintsExtension(false, false, 0, false))
    request.CertificateExtensions.Add(X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature ||| X509KeyUsageFlags.KeyEncipherment, true))
    let serverAuth = OidCollection()
    serverAuth.Add(Oid "1.3.6.1.5.5.7.3.1") |> ignore
    request.CertificateExtensions.Add(X509EnhancedKeyUsageExtension(serverAuth, false))
    request.CertificateExtensions.Add(subjectNames host addresses)
    let now = DateTimeOffset.UtcNow
    let notBefore, notAfter = now.AddDays -1.0, now.AddDays 397.0
    if selfSigned then
        use cert = request.CreateSelfSigned(notBefore, notAfter)
        withUsableKey cert
    else
        use ca = authority folder
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false))
        let serial = RandomNumberGenerator.GetBytes 16
        serial[0] <- serial[0] &&& 0x7Fuy // positive
        // Inside the authority's own validity (made moments earlier on a first run).
        let notBefore = if notBefore < DateTimeOffset ca.NotBefore then DateTimeOffset ca.NotBefore else notBefore
        let notAfter = if notAfter > DateTimeOffset ca.NotAfter then DateTimeOffset ca.NotAfter else notAfter
        use signed = request.Create(ca, notBefore, notAfter, serial)
        use withKey = signed.CopyWithPrivateKey key
        withUsableKey withKey
