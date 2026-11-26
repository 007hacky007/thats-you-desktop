package server

import (
	"encoding/base64"
	"fmt"
	"net"
	"strconv"
	"strings"
)

// BuildJoinURL builds the QR payload of contract 2.4:
// <base without fragment>#h=..&p=..&u=..&w=..&f=..&t=.. (keys in this order).
func BuildJoinURL(base, ip string, port int, ufrag, pwd string, fingerprint []byte, token string) (string, error) {
	if i := strings.IndexByte(base, '#'); i >= 0 {
		base = base[:i]
	}
	if !strings.HasPrefix(base, "https://") && !strings.HasPrefix(base, "http://") {
		return "", fmt.Errorf("%w: base URL must start with http:// or https://", ErrArg)
	}
	if len(base) > maxBaseURL {
		return "", fmt.Errorf("%w: base URL longer than %d bytes", ErrArg, maxBaseURL)
	}
	for i := 0; i < len(base); i++ {
		if c := base[i]; c <= ' ' || c == 0x7f {
			return "", fmt.Errorf("%w: base URL contains whitespace or control characters", ErrArg)
		}
	}
	if p := net.ParseIP(ip); p == nil || p.To4() == nil {
		return "", fmt.Errorf("%w: bad IPv4 %q", ErrArg, ip)
	}
	if port < 1 || port > 65535 {
		return "", fmt.Errorf("%w: port %d", ErrArg, port)
	}
	if len(fingerprint) != 32 {
		return "", fmt.Errorf("%w: fingerprint of %d bytes", ErrArg, len(fingerprint))
	}
	var b strings.Builder
	b.WriteString(base)
	b.WriteString("#h=")
	b.WriteString(ip)
	b.WriteString("&p=")
	b.WriteString(strconv.Itoa(port))
	b.WriteString("&u=")
	b.WriteString(ufrag)
	b.WriteString("&w=")
	b.WriteString(pwd)
	b.WriteString("&f=")
	b.WriteString(base64.RawURLEncoding.EncodeToString(fingerprint))
	b.WriteString("&t=")
	b.WriteString(token)
	return b.String(), nil
}
