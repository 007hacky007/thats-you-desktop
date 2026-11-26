package server

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/sha256"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"errors"
	"fmt"
	"math/big"
	"os"
	"path/filepath"
	"time"
)

const (
	certValidity    = 10 * 365 * 24 * time.Hour
	certRenewBefore = 30 * 24 * time.Hour
)

// Cert is the persistent DTLS identity of the game.
type Cert struct {
	Key  *ecdsa.PrivateKey
	X509 *x509.Certificate
}

// Fingerprint is the SHA-256 of the DER certificate (32 bytes).
func (c *Cert) Fingerprint() []byte {
	s := sha256.Sum256(c.X509.Raw)
	return s[:]
}

// CertStatus says what LoadOrCreateCert had to do.
type CertStatus int

const (
	CertLoaded      CertStatus = iota // existing file used
	CertCreated                       // file did not exist
	CertRegenerated                   // file was unparsable or about to expire
)

// LoadOrCreateCert loads the PEM certificate and PKCS#8 key at path, or
// creates a new ECDSA P-256 self-signed one (parent directories created,
// file mode 0600) when the file is missing, unparsable or expires within
// 30 days.
func LoadOrCreateCert(path string, now time.Time) (*Cert, CertStatus, error) {
	if path == "" {
		return nil, 0, fmt.Errorf("%w: empty certificate path", ErrArg)
	}
	data, err := os.ReadFile(path)
	status := CertCreated
	switch {
	case err == nil:
		c, perr := parseCertPEM(data)
		if perr == nil && c.X509.NotAfter.Sub(now) > certRenewBefore && !now.Before(c.X509.NotBefore) {
			return c, CertLoaded, nil
		}
		status = CertRegenerated
	case errors.Is(err, os.ErrNotExist):
	default:
		return nil, 0, fmt.Errorf("%w: read %s: %v", ErrCert, path, err)
	}
	c, pemData, err := newCert(now)
	if err != nil {
		return nil, 0, fmt.Errorf("%w: %v", ErrCert, err)
	}
	if err := writeFile0600(path, pemData); err != nil {
		return nil, 0, fmt.Errorf("%w: %v", ErrCert, err)
	}
	return c, status, nil
}

func parseCertPEM(data []byte) (*Cert, error) {
	var c Cert
	for {
		var b *pem.Block
		b, data = pem.Decode(data)
		if b == nil {
			break
		}
		switch b.Type {
		case "CERTIFICATE":
			x, err := x509.ParseCertificate(b.Bytes)
			if err != nil {
				return nil, err
			}
			c.X509 = x
		case "PRIVATE KEY":
			k, err := x509.ParsePKCS8PrivateKey(b.Bytes)
			if err != nil {
				return nil, err
			}
			ek, ok := k.(*ecdsa.PrivateKey)
			if !ok {
				return nil, errors.New("key is not ECDSA")
			}
			c.Key = ek
		}
	}
	if c.X509 == nil || c.Key == nil {
		return nil, errors.New("certificate or key missing")
	}
	pub, ok := c.X509.PublicKey.(*ecdsa.PublicKey)
	if !ok || !pub.Equal(&c.Key.PublicKey) || pub.Curve != elliptic.P256() {
		return nil, errors.New("key does not match a P-256 certificate")
	}
	return &c, nil
}

func newCert(now time.Time) (*Cert, []byte, error) {
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		return nil, nil, err
	}
	serial, err := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 63))
	if err != nil {
		return nil, nil, err
	}
	tmpl := &x509.Certificate{
		SerialNumber:          serial,
		Subject:               pkix.Name{CommonName: "iwyknet"},
		NotBefore:             now.Add(-24 * time.Hour),
		NotAfter:              now.Add(certValidity),
		KeyUsage:              x509.KeyUsageDigitalSignature,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth, x509.ExtKeyUsageClientAuth},
		BasicConstraintsValid: true,
	}
	der, err := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	if err != nil {
		return nil, nil, err
	}
	x, err := x509.ParseCertificate(der)
	if err != nil {
		return nil, nil, err
	}
	pk, err := x509.MarshalPKCS8PrivateKey(key)
	if err != nil {
		return nil, nil, err
	}
	out := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})
	out = append(out, pem.EncodeToMemory(&pem.Block{Type: "PRIVATE KEY", Bytes: pk})...)
	return &Cert{Key: key, X509: x}, out, nil
}

func writeFile0600(path string, data []byte) error {
	dir := filepath.Dir(path)
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return err
	}
	f, err := os.CreateTemp(dir, ".iwyknet-cert-*")
	if err != nil {
		return err
	}
	tmp := f.Name()
	defer os.Remove(tmp)
	if err := f.Chmod(0o600); err != nil && !isWindows() {
		f.Close()
		return err
	}
	if _, err := f.Write(data); err != nil {
		f.Close()
		return err
	}
	if err := f.Close(); err != nil {
		return err
	}
	if err := os.Rename(tmp, path); err != nil {
		// Windows cannot rename over an open or read-only file: try a plain write.
		return os.WriteFile(path, data, 0o600)
	}
	return nil
}
