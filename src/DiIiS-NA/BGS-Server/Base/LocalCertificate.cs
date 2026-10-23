using DiIiS_NA.Core.Logging;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DiIiS_NA.LoginServer.Base
{
	/// <summary>
	/// Certificate for the Battle.net TLS listener, generated per machine instead of shipping a
	/// shared one whose private key is public. The signing CA's private key only exists in memory
	/// while the server certificate is issued, so nothing can mint further certificates from it.
	/// </summary>
	public static class LocalCertificate
	{
		private static readonly Logger Logger = LogManager.CreateLogger("Certificate");

		private const string PfxPassword = "123";

		private static readonly string[] HostNames =
		{
			"us.actual.battle.net", "eu.actual.battle.net", "kr.actual.battle.net", "cn.actual.battle.net", "localhost"
		};

		public static string Folder => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3Offline");
		public static string PfxPath => Path.Combine(Folder, "bnetserver.p12");
		public static string CaCertPath => Path.Combine(Folder, "D3Offline-LocalCA.cer");

		public static X509Certificate2 Load() => X509CertificateLoader.LoadPkcs12FromFile(PfxPath, PfxPassword);

		public static void EnsureExists()
		{
			if (File.Exists(PfxPath))
			{
				Logger.Info("Using local certificate: {0}", PfxPath);
				return;
			}

			Directory.CreateDirectory(Folder);
			var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
			var notAfter = notBefore.AddYears(5);

			using var caKey = RSA.Create(2048);
			var caRequest = new CertificateRequest($"CN=D3Offline Local CA ({Environment.MachineName})", caKey,
				HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
			caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
			caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
			using var ca = caRequest.CreateSelfSigned(notBefore, notAfter);

			using var serverKey = RSA.Create(2048);
			var serverRequest = new CertificateRequest($"CN={HostNames[0]}", serverKey,
				HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var san = new SubjectAlternativeNameBuilder();
			foreach (var host in HostNames)
				san.AddDnsName(host);
			san.AddIpAddress(IPAddress.Loopback);
			serverRequest.CertificateExtensions.Add(san.Build());
			serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
			serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
			serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
			serverRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));

			var serial = RandomNumberGenerator.GetBytes(16);
			serial[0] &= 0x7F;
			using var issued = serverRequest.Create(ca, notBefore, notAfter.AddDays(-1), serial);
			using var server = issued.CopyWithPrivateKey(serverKey);

			var caPublic = ca.Export(X509ContentType.Cert);
			var bundle = new X509Certificate2Collection { server, X509CertificateLoader.LoadCertificate(caPublic) };
			File.WriteAllBytes(PfxPath, bundle.Export(X509ContentType.Pkcs12, PfxPassword));
			File.WriteAllBytes(CaCertPath, caPublic);

			Logger.Success("Generated a new local certificate in {0}", Folder);
			Logger.Warn("Trust {0} (Current User > Trusted Root Certification Authorities) before connecting the game.", CaCertPath);
		}
	}
}
