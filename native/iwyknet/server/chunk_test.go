package server

import (
	"bytes"
	"encoding/binary"
	"errors"
	"testing"
)

func frame(id int32, total, off uint32, n int) []byte {
	f := make([]byte, ChunkHeaderLen+n)
	binary.LittleEndian.PutUint32(f[0:], uint32(id))
	binary.LittleEndian.PutUint32(f[4:], total)
	binary.LittleEndian.PutUint32(f[8:], off)
	for i := range n {
		f[ChunkHeaderLen+i] = byte(off) + byte(i)
	}
	return f
}

func pattern(n int) []byte {
	b := make([]byte, n)
	for i := range b {
		b[i] = byte(i*7 + i/251)
	}
	return b
}

func TestEncodeChunksRoundTrip(t *testing.T) {
	for _, n := range []int{1, 16383, 16384, 16385, 65536, 131071, 131072} {
		data := pattern(n)
		frames, err := EncodeChunks(77, data)
		if err != nil {
			t.Fatalf("%d: %v", n, err)
		}
		if want := (n + Chunk - 1) / Chunk; len(frames) != want {
			t.Fatalf("%d: %d frames, want %d", n, len(frames), want)
		}
		var r Reassembler
		for i, f := range frames {
			if len(f) > ChunkHeaderLen+Chunk {
				t.Fatalf("frame too large %d", len(f))
			}
			if binary.LittleEndian.Uint32(f[8:]) != uint32(i*Chunk) {
				t.Fatalf("offset of frame %d", i)
			}
			id, got, done, err := r.Add(f)
			if err != nil || id != 77 {
				t.Fatalf("%d: add %d: %v id %d", n, i, err, id)
			}
			if done != (i == len(frames)-1) {
				t.Fatalf("%d: done=%v at %d", n, done, i)
			}
			if done && !bytes.Equal(got, data) {
				t.Fatalf("%d: content mismatch", n)
			}
		}
		if r.Pending() != 0 {
			t.Fatalf("pending left")
		}
	}
}

func TestEncodeChunksErrors(t *testing.T) {
	if _, err := EncodeChunks(0, []byte{1}); !errors.Is(err, ErrArg) {
		t.Fatalf("id 0: %v", err)
	}
	if _, err := EncodeChunks(-5, []byte{1}); !errors.Is(err, ErrArg) {
		t.Fatalf("id -5: %v", err)
	}
	if _, err := EncodeChunks(1, nil); !errors.Is(err, ErrTooBig) {
		t.Fatalf("empty: %v", err)
	}
	if _, err := EncodeChunks(1, make([]byte, MaxBinary+1)); !errors.Is(err, ErrTooBig) {
		t.Fatalf("too big: %v", err)
	}
	if _, err := EncodeChunks(2147483647, []byte{1}); err != nil {
		t.Fatalf("max id: %v", err)
	}
}

func TestReassemblerErrors(t *testing.T) {
	cases := []struct {
		name   string
		frames [][]byte
	}{
		{"short header", [][]byte{make([]byte, 11)}},
		{"no payload", [][]byte{frame(1, 10, 0, 0)}},
		{"id zero", [][]byte{frame(0, 10, 0, 10)}},
		{"id negative", [][]byte{frame(-3, 10, 0, 10)}},
		{"total zero", [][]byte{frame(1, 0, 0, 1)}},
		{"total over max", [][]byte{frame(1, MaxBinary+1, 0, 100)}},
		{"payload over chunk", [][]byte{frame(1, 20000, 0, Chunk+1)}},
		{"past total", [][]byte{frame(1, 10, 0, 11)}},
		{"first offset not zero", [][]byte{frame(1, 20, 10, 10)}},
		{"gap", [][]byte{frame(1, 30, 0, 10), frame(1, 30, 20, 10)}},
		{"repeat", [][]byte{frame(1, 30, 0, 10), frame(1, 30, 0, 10)}},
		{"total changed", [][]byte{frame(1, 30, 0, 10), frame(1, 31, 10, 10)}},
		{"fifth partial", [][]byte{
			frame(1, 30, 0, 10), frame(2, 30, 0, 10), frame(3, 30, 0, 10),
			frame(4, 30, 0, 10), frame(5, 30, 0, 10)}},
	}
	for _, c := range cases {
		var r Reassembler
		var err error
		for _, f := range c.frames {
			if _, _, _, err = r.Add(f); err != nil {
				break
			}
		}
		if !errors.Is(err, ErrProtocol) {
			t.Errorf("%s: got %v, want protocol error", c.name, err)
		}
	}
}

func TestReassemblerFourPartialsAndInterleave(t *testing.T) {
	var r Reassembler
	for id := int32(1); id <= 4; id++ {
		if _, _, done, err := r.Add(frame(id, 20, 0, 10)); err != nil || done {
			t.Fatalf("start %d: %v", id, err)
		}
	}
	if r.Pending() != 4 {
		t.Fatalf("pending %d", r.Pending())
	}
	// A single-chunk transfer does not occupy a partial slot.
	if _, _, done, err := r.Add(frame(9, 5, 0, 5)); err != nil || !done {
		t.Fatalf("single chunk with 4 partials: %v %v", done, err)
	}
	for id := int32(4); id >= 1; id-- {
		got, data, done, err := r.Add(frame(id, 20, 10, 10))
		if err != nil || !done || got != id || len(data) != 20 {
			t.Fatalf("finish %d: %v %v %d", id, err, done, len(data))
		}
	}
	// The id can be reused after completion.
	if _, _, _, err := r.Add(frame(1, 20, 0, 10)); err != nil {
		t.Fatalf("reuse: %v", err)
	}
}
