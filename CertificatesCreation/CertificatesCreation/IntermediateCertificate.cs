﻿using CertificatesCreation.Models;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CertificatesCreation
{
    public class IntermediateCertificate
    {
        public X509Certificate2 CreateIntermediateCertificate(
            DistinguishedName distinguishedName,
            BasicConstraints basicConstraints,
            ValidityPeriod validityPeriod,
            X509Certificate2 parentCertificateAuthority)
        {
            if(parentCertificateAuthority == null)
            {
                throw new ArgumentNullException("The issuing Certificate Authority is required");
            }

            using (var ecdsa = ECDsa.Create("ECDsa"))
            {
                ecdsa.KeySize = 256;
                var request = new CertificateRequest(
                    Certificates.CreateIssuerOrSubject(distinguishedName), 
                    ecdsa, 
                    HashAlgorithmName.SHA256);

                // set basic certificate contraints
                Certificates.AddBasicConstraints(request, basicConstraints);

                // key usage: Digital Signature and Key Encipherment
                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension( X509KeyUsageFlags.KeyCertSign, true));

                // set the AuthorityKeyIdentifier. There is no built-in 
                // support, so it needs to be copied from the Subject Key 
                // Identifier of the signing certificate and massaged slightly.
                // AuthorityKeyIdentifier is "KeyID=<subject key identifier>"
                var issuerSubjectKey = parentCertificateAuthority.Extensions["Subject Key Identifier"].RawData;
                var segment = new ArraySegment<byte>(issuerSubjectKey, 2, issuerSubjectKey.Length - 2);
                var authorityKeyIdentifier = new byte[segment.Count + 4];
                // these bytes define the "KeyID" part of the AuthorityKeyIdentifer
                authorityKeyIdentifier[0] = 0x30;
                authorityKeyIdentifier[1] = 0x16;
                authorityKeyIdentifier[2] = 0x80;
                authorityKeyIdentifier[3] = 0x14;
                segment.CopyTo(authorityKeyIdentifier, 4);
                request.CertificateExtensions.Add(new X509Extension("2.5.29.35", authorityKeyIdentifier, false));
               
                // in addition to the subject name
                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName(distinguishedName.CommonName);
                var sanExtension = sanBuilder.Build();
                request.CertificateExtensions.Add(sanExtension);

                // Enhanced key usages
                request.CertificateExtensions.Add(
                    new X509EnhancedKeyUsageExtension(
                        new OidCollection {
                            new Oid("1.3.6.1.5.5.7.3.2"), // TLS Client auth
                            new Oid("1.3.6.1.5.5.7.3.1")  // TLS Server auth
                        },
                        false));

                // add this subject key identifier
                request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

                // certificate expiry: Valid from Yesterday to Now+365 days
                // Unless the signing cert's validity is less. It's not possible
                // to create a cert with longer validity than the signing cert.
                var notbefore = validityPeriod.ValidFrom.AddDays(-1);
                if (notbefore < parentCertificateAuthority.NotBefore)
                {
                    notbefore = new DateTimeOffset(parentCertificateAuthority.NotBefore);
                }

                var notafter = validityPeriod.ValidTo;
                if (notafter > parentCertificateAuthority.NotAfter)
                {
                    notafter = new DateTimeOffset(parentCertificateAuthority.NotAfter);
                }

                // cert serial is the epoch/unix timestamp
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var unixTime = Convert.ToInt64((DateTime.UtcNow - epoch).TotalSeconds);
                var serial = BitConverter.GetBytes(unixTime);

                X509Certificate2 generatedCertificate = request.Create(parentCertificateAuthority, notbefore, notafter, serial);
                return generatedCertificate.CopyWithPrivateKey(ecdsa);

            }
        }
    }
}