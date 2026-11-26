package server

import (
	"errors"
	"strings"
	"testing"
)

func TestQR(t *testing.T) {
	side, m, err := QR("https://example.invalid/iwyk/#h=192.168.1.23&p=47000&u=abcdEFGH&w=abcdefghijklmnopqrstuvwx&f=8PHy8_T19vf4-fr7_P3-_wABAgMEBQYHCAkKCwwNDg8&t=0123456789abcdef")
	if err != nil {
		t.Fatal(err)
	}
	if side < 21 || (side-21)%4 != 0 || len(m) != side*side {
		t.Fatalf("side %d len %d", side, len(m))
	}
	// Finder pattern in the top-left corner without quiet zone: row 0 is
	// 7 dark modules then a light separator.
	for x := 0; x < 7; x++ {
		if m[x] != 1 {
			t.Fatalf("finder row 0 col %d", x)
		}
	}
	if m[7] != 0 || m[side+1] != 0 || m[2*side+2] != 1 {
		t.Fatal("finder pattern shape")
	}
	small, _, _ := QR("x")
	if small != 21 {
		t.Fatalf("version 1 side %d", small)
	}
	if _, _, err := QR(""); !errors.Is(err, ErrArg) {
		t.Fatalf("empty: %v", err)
	}
	if _, _, err := QR(strings.Repeat("x", 5000)); !errors.Is(err, ErrArg) {
		t.Fatalf("too long: %v", err)
	}
	big, mm, err := QR(strings.Repeat("y", 2300))
	if err != nil || big > 177 || len(mm) > QRMaxModules {
		t.Fatalf("large: %d %v", big, err)
	}
}
