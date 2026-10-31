"""Generate disposable localhost DTLS credentials. Never changes a VPS or trust store."""
from pathlib import Path
import sys,datetime,json,secrets,ipaddress
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'tools/ssh_libs'))
from cryptography import x509
from cryptography.x509.oid import NameOID
from cryptography.hazmat.primitives import hashes,serialization
from cryptography.hazmat.primitives.asymmetric import rsa
folder=root/'tools/realtime-integration/credentials';folder.mkdir(parents=True,exist_ok=True)
key=rsa.generate_private_key(public_exponent=65537,key_size=2048)
subject=x509.Name([x509.NameAttribute(NameOID.COMMON_NAME,'localhost')]);now=datetime.datetime.now(datetime.timezone.utc)
ca_key=rsa.generate_private_key(public_exponent=65537,key_size=2048)
ca_name=x509.Name([x509.NameAttribute(NameOID.COMMON_NAME,'HBRP local test CA')])
ca=(x509.CertificateBuilder().subject_name(ca_name).issuer_name(ca_name).public_key(ca_key.public_key()).serial_number(x509.random_serial_number())
 .not_valid_before(now-datetime.timedelta(minutes=5)).not_valid_after(now+datetime.timedelta(days=7))
 .add_extension(x509.BasicConstraints(ca=True,path_length=0),critical=True).sign(ca_key,hashes.SHA256()))
cert=(x509.CertificateBuilder().subject_name(subject).issuer_name(ca_name).public_key(key.public_key()).serial_number(x509.random_serial_number())
 .not_valid_before(now-datetime.timedelta(minutes=5)).not_valid_after(now+datetime.timedelta(days=7))
 .add_extension(x509.BasicConstraints(ca=False,path_length=None),critical=True)
 .add_extension(x509.SubjectAlternativeName([x509.DNSName('localhost'),x509.IPAddress(ipaddress.ip_address('127.0.0.1'))]),critical=False)
 .sign(ca_key,hashes.SHA256()))
(folder/'localhost-ca.pem').write_bytes(ca.public_bytes(serialization.Encoding.PEM))
(folder/'localhost.pem').write_bytes(cert.public_bytes(serialization.Encoding.PEM))
(folder/'localhost.key').write_bytes(key.private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.PKCS8,serialization.NoEncryption()))
(folder/'test-secret.txt').write_text(secrets.token_urlsafe(48),encoding='utf-8')
(folder/'server.json').write_text(json.dumps({'bindAddress':'127.0.0.1','port':7777,'certificateFile':'localhost.pem','privateKeyFile':'localhost.key'},indent=2),encoding='utf-8')
print('LOCAL_DTLS_CREDENTIALS_READY (localhost only; private files excluded from Git)')
