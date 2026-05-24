using System.Security.Cryptography;

var pub = RSA.Create(2048).ExportSubjectPublicKeyInfo();
Console.WriteLine(Convert.ToBase64String(pub));
