using System;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Security;

namespace Kmart;

public class SignatureTools
{
    public static ECPoint Recover(byte[] hash, byte[] sigBytes, int rec)
    {
        BigInteger r = new BigInteger(1, sigBytes, 0, 32);
        BigInteger s = new BigInteger(1, sigBytes, 32, 32);
        BigInteger[] sig = new BigInteger[] {r, s};
        ECPoint Q = ECDSA_SIG_recover_key_GFp(sig, hash, rec, true);
        return Q;
    }

    public static ECPoint ECDSA_SIG_recover_key_GFp(BigInteger[] sig, byte[] hash, int recid, bool check)
    {
        X9ECParameters ecParams = Org.BouncyCastle.Asn1.Sec.SecNamedCurves.GetByName("secp256k1");
        int i = recid / 2;

        BigInteger order = ecParams.N;
        BigInteger field = (ecParams.Curve as FpCurve)!.Q;
        BigInteger x = order.Multiply(new BigInteger(i.ToString())).Add(sig[0]);
        if (x.CompareTo(field) >= 0) throw new Exception("X too large");

        byte[] compressedPoint = new Byte[x.ToByteArrayUnsigned().Length + 1];
        compressedPoint[0] = (byte) (0x02 + (recid % 2));
        Buffer.BlockCopy(x.ToByteArrayUnsigned(), 0, compressedPoint, 1, compressedPoint.Length - 1);
        ECPoint R = ecParams.Curve.DecodePoint(compressedPoint);

        if (check)
        {
            ECPoint O = R.Multiply(order);
            if (!O.IsInfinity) throw new Exception("Check failed");
        }

        int n = (ecParams.Curve as FpCurve)!.Q.ToByteArrayUnsigned().Length * 8;
        BigInteger e = new BigInteger(1, hash);
        if (8 * hash.Length > n)
        {
            e = e.ShiftRight(8 - (n & 7));
        }

        e = BigInteger.Zero.Subtract(e).Mod(order);
        BigInteger rr = sig[0].ModInverse(order);
        BigInteger sor = sig[1].Multiply(rr).Mod(order);
        BigInteger eor = e.Multiply(rr).Mod(order);
        ECPoint Q = ecParams.G.Multiply(eor).Add(R.Multiply(sor));

        return Q;
    }

    public static bool VerifySignature(byte[] pubkey, byte[] hash, byte[] sigBytes)
    {
        X9ECParameters ecParams = Org.BouncyCastle.Asn1.Sec.SecNamedCurves.GetByName("secp256k1");
        ECDomainParameters domainParameters = new ECDomainParameters(ecParams.Curve,
            ecParams.G, ecParams.N, ecParams.H,
            ecParams.GetSeed());

        BigInteger r = new BigInteger(1, sigBytes, 0, 32);
        BigInteger s = new BigInteger(1, sigBytes, 32, 32);
        ECPublicKeyParameters publicKey =
            new ECPublicKeyParameters(ecParams.Curve.DecodePoint(pubkey), domainParameters);

        ECDsaSigner signer = new ECDsaSigner();
        signer.Init(false, publicKey);
        return signer.VerifySignature(hash, r, s);
    }

    public static AsymmetricCipherKeyPair GenerateKeypair()
    {
        X9ECParameters ecParams = Org.BouncyCastle.Asn1.Sec.SecNamedCurves.GetByName("secp256k1");
        ECDomainParameters domainParameters = new ECDomainParameters(ecParams.Curve,
            ecParams.G, ecParams.N, ecParams.H,
            ecParams.GetSeed());
        ECKeyGenerationParameters keyGenParams =
            new ECKeyGenerationParameters(domainParameters, new SecureRandom());

        AsymmetricCipherKeyPair keyPair;
        ECKeyPairGenerator generator = new ECKeyPairGenerator();
        generator.Init(keyGenParams);
        keyPair = generator.GenerateKeyPair();

        return keyPair;
    }

    public static byte[] GenerateSignature(byte[] hash, byte[] privkey)
    {
        X9ECParameters ecParams = Org.BouncyCastle.Asn1.Sec.SecNamedCurves.GetByName("secp256k1");
        ECDomainParameters domainParameters = new ECDomainParameters(ecParams.Curve,
            ecParams.G, ecParams.N, ecParams.H,
            ecParams.GetSeed());
        var privateKey = new ECPrivateKeyParameters(new BigInteger(1, privkey, 0, 32), domainParameters);

        ECDsaSigner signer = new ECDsaSigner();
        signer.Init(true, privateKey);
        BigInteger[] sig = signer.GenerateSignature(hash);
        var bytes = new byte[64];
        var sig0 = sig[0].ToByteArrayUnsigned();
        var sig1 = sig[1].ToByteArrayUnsigned();
        Buffer.BlockCopy(sig0, 0, bytes, 32 - sig0.Length, sig0.Length);
        Buffer.BlockCopy(sig1, 0, bytes, 64 - sig1.Length, sig1.Length);
        return bytes;
    }

    public static byte[] GenerateSignature(byte[] hash, AsymmetricCipherKeyPair pair)
    {
        ECDsaSigner signer = new ECDsaSigner();
        signer.Init(true, pair.Private);
        BigInteger[] sig = signer.GenerateSignature(hash);
        var bytes = new byte[64];
        Buffer.BlockCopy(sig[0].ToByteArrayUnsigned(), 0, bytes, 0, 32);
        Buffer.BlockCopy(sig[1].ToByteArrayUnsigned(), 0, bytes, 32, 32);
        return bytes;
    }

    public static bool VerifySignature(byte[] hash, byte[] sigBytes)
    {
        var sig = new BigInteger[2];
        sig[0] = new BigInteger(1, sigBytes, 0, 32);
        sig[1] = new BigInteger(1, sigBytes, 32, 32);
        for (int rec = 0; rec < 4; rec++)
        {
            try
            {
                ECPoint Q = ECDSA_SIG_recover_key_GFp(sig, hash, rec, true);
                ECPoint genQ = ECDSA_SIG_recover_key_GFp(sig, hash, rec, false);
                bool verifies = VerifySignature(genQ.GetEncoded(), hash, sigBytes);

                if (verifies)
                    return true;
            }
            catch { }
        }
        
        return false;
    }
}