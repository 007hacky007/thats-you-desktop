package server

import (
	"bytes"
	"crypto/rand"
	"crypto/subtle"
	"encoding/json"
	"fmt"
	"unicode"
	"unicode/utf8"
)

// CheckHello validates the first frame of a session (contract 3.3) and
// returns the device uid.
func CheckHello(frame []byte, token string) (string, error) {
	if len(frame) > MaxHello {
		return "", fmt.Errorf("%w: hello of %d bytes", ErrProtocol, len(frame))
	}
	if !utf8.Valid(frame) {
		return "", fmt.Errorf("%w: hello is not UTF-8", ErrProtocol)
	}
	trimmed := bytes.TrimSpace(frame)
	if len(trimmed) == 0 || trimmed[0] != '{' {
		return "", fmt.Errorf("%w: hello is not a JSON object", ErrProtocol)
	}
	var m struct {
		T     *string `json:"t"`
		Token *string `json:"token"`
		UID   *string `json:"uid"`
	}
	if err := json.Unmarshal(frame, &m); err != nil {
		return "", fmt.Errorf("%w: hello: %v", ErrProtocol, err)
	}
	if m.T == nil || *m.T != "hello" {
		return "", fmt.Errorf("%w: first frame is not a hello", ErrProtocol)
	}
	if m.Token == nil || subtle.ConstantTimeCompare([]byte(*m.Token), []byte(token)) != 1 {
		return "", fmt.Errorf("%w: wrong token", ErrProtocol)
	}
	if m.UID == nil {
		return "", fmt.Errorf("%w: hello without uid", ErrProtocol)
	}
	uid := *m.UID
	if len(uid) < 1 || len(uid) > MaxUID {
		return "", fmt.Errorf("%w: uid of %d bytes", ErrProtocol, len(uid))
	}
	for _, r := range uid {
		if unicode.IsControl(r) || r == utf8.RuneError {
			return "", fmt.Errorf("%w: uid has control characters", ErrProtocol)
		}
	}
	return uid, nil
}

const alnum = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"

// RandomString returns n characters from [A-Za-z0-9] from crypto/rand,
// without modulo bias.
func RandomString(n int) string {
	out := make([]byte, 0, n)
	var b [64]byte
	for len(out) < n {
		if _, err := rand.Read(b[:]); err != nil {
			panic(err)
		}
		for _, c := range b {
			if c < 248 && len(out) < n { // 248 = 4 * 62
				out = append(out, alnum[int(c)%len(alnum)])
			}
		}
	}
	return string(out)
}
