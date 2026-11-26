package server

import (
	"bytes"
	"errors"
	"os"
	"path/filepath"
	"runtime"
	"testing"
	"time"
)

func TestLoadOrCreateCert(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "a", "b", "dtls.pem")
	now := time.Date(2026, 11, 26, 12, 0, 0, 0, time.UTC)

	c1, st, err := LoadOrCreateCert(path, now)
	if err != nil || st != CertCreated {
		t.Fatalf("create: %v %v", st, err)
	}
	if len(c1.Fingerprint()) != 32 {
		t.Fatal("fingerprint length")
	}
	if runtime.GOOS != "windows" {
		fi, err := os.Stat(path)
		if err != nil || fi.Mode().Perm() != 0o600 {
			t.Fatalf("mode %v %v", fi.Mode(), err)
		}
	}
	if d := c1.X509.NotAfter.Sub(now); d < 9*365*24*time.Hour {
		t.Fatalf("validity %v", d)
	}

	c2, st, err := LoadOrCreateCert(path, now.Add(time.Hour))
	if err != nil || st != CertLoaded || !bytes.Equal(c1.Fingerprint(), c2.Fingerprint()) {
		t.Fatalf("reload: %v %v", st, err)
	}

	// Expiring within 30 days -> regenerated with a new fingerprint.
	late := c1.X509.NotAfter.Add(-29 * 24 * time.Hour)
	c3, st, err := LoadOrCreateCert(path, late)
	if err != nil || st != CertRegenerated || bytes.Equal(c1.Fingerprint(), c3.Fingerprint()) {
		t.Fatalf("expiry: %v %v", st, err)
	}
	c4, st, _ := LoadOrCreateCert(path, late)
	if st != CertLoaded || !bytes.Equal(c3.Fingerprint(), c4.Fingerprint()) {
		t.Fatalf("after regeneration: %v", st)
	}

	// Garbage -> regenerated.
	if err := os.WriteFile(path, []byte("not a pem"), 0o600); err != nil {
		t.Fatal(err)
	}
	if _, st, err := LoadOrCreateCert(path, now); err != nil || st != CertRegenerated {
		t.Fatalf("garbage: %v %v", st, err)
	}

	// Certificate without key -> regenerated.
	data, _ := os.ReadFile(path)
	certOnly := data[:bytes.Index(data, []byte("-----BEGIN PRIVATE KEY"))]
	os.WriteFile(path, certOnly, 0o600)
	if _, st, err := LoadOrCreateCert(path, now); err != nil || st != CertRegenerated {
		t.Fatalf("cert only: %v %v", st, err)
	}

	// Mismatched key -> regenerated.
	other := filepath.Join(dir, "other.pem")
	LoadOrCreateCert(other, now)
	od, _ := os.ReadFile(other)
	d, _ := os.ReadFile(path)
	mixed := append(append([]byte{}, d[:bytes.Index(d, []byte("-----BEGIN PRIVATE KEY"))]...), od[bytes.Index(od, []byte("-----BEGIN PRIVATE KEY")):]...)
	os.WriteFile(path, mixed, 0o600)
	if _, st, err := LoadOrCreateCert(path, now); err != nil || st != CertRegenerated {
		t.Fatalf("mixed: %v %v", st, err)
	}
}

func TestLoadOrCreateCertErrors(t *testing.T) {
	if _, _, err := LoadOrCreateCert("", time.Now()); !errors.Is(err, ErrArg) {
		t.Fatalf("empty path: %v", err)
	}
	dir := t.TempDir()
	blocker := filepath.Join(dir, "file")
	os.WriteFile(blocker, []byte("x"), 0o600)
	if _, _, err := LoadOrCreateCert(filepath.Join(blocker, "dtls.pem"), time.Now()); !errors.Is(err, ErrCert) {
		t.Fatalf("parent is a file: %v", err)
	}
	// A directory at the cert path cannot be read as a file.
	if _, _, err := LoadOrCreateCert(dir, time.Now()); !errors.Is(err, ErrCert) {
		t.Fatalf("path is a dir: %v", err)
	}
}
