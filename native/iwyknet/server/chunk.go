package server

import (
	"encoding/binary"
	"fmt"
)

// EncodeChunks splits one content transfer into binary frames (contract 2.2):
// 12-byte header (transfer_id int32 LE, total_len uint32 LE, offset uint32 LE)
// followed by at most Chunk payload bytes.
func EncodeChunks(transferID int32, data []byte) ([][]byte, error) {
	if transferID <= 0 {
		return nil, fmt.Errorf("%w: transfer id %d", ErrArg, transferID)
	}
	if len(data) == 0 {
		return nil, fmt.Errorf("%w: empty content", ErrTooBig)
	}
	if len(data) > MaxBinary {
		return nil, fmt.Errorf("%w: content %d > %d bytes", ErrTooBig, len(data), MaxBinary)
	}
	var out [][]byte
	for off := 0; off < len(data); off += Chunk {
		n := min(Chunk, len(data)-off)
		f := make([]byte, ChunkHeaderLen+n)
		binary.LittleEndian.PutUint32(f[0:], uint32(transferID))
		binary.LittleEndian.PutUint32(f[4:], uint32(len(data)))
		binary.LittleEndian.PutUint32(f[8:], uint32(off))
		copy(f[ChunkHeaderLen:], data[off:off+n])
		out = append(out, f)
	}
	return out, nil
}

type partial struct {
	total int
	buf   []byte
}

// Reassembler rebuilds content transfers from binary frames. Any error means
// the sender is broken; the caller drops the transfer or the session.
type Reassembler struct {
	parts map[int32]*partial
}

// Pending reports the number of partial transfers.
func (r *Reassembler) Pending() int { return len(r.parts) }

// Add consumes one binary frame. When it completes a transfer, done is true
// and data holds the whole content.
func (r *Reassembler) Add(frame []byte) (transferID int32, data []byte, done bool, err error) {
	if len(frame) <= ChunkHeaderLen {
		return 0, nil, false, fmt.Errorf("%w: chunk of %d bytes has no payload", ErrProtocol, len(frame))
	}
	id := int32(binary.LittleEndian.Uint32(frame[0:]))
	total := binary.LittleEndian.Uint32(frame[4:])
	off := binary.LittleEndian.Uint32(frame[8:])
	n := len(frame) - ChunkHeaderLen
	switch {
	case id <= 0:
		return id, nil, false, fmt.Errorf("%w: transfer id %d", ErrProtocol, id)
	case total == 0 || total > MaxBinary:
		return id, nil, false, fmt.Errorf("%w: total_len %d", ErrProtocol, total)
	case n > Chunk:
		return id, nil, false, fmt.Errorf("%w: chunk payload %d > %d", ErrProtocol, n, Chunk)
	case uint64(off)+uint64(n) > uint64(total):
		return id, nil, false, fmt.Errorf("%w: chunk %d+%d past total %d", ErrProtocol, off, n, total)
	}
	if r.parts == nil {
		r.parts = map[int32]*partial{}
	}
	p := r.parts[id]
	if p == nil {
		if off != 0 {
			return id, nil, false, fmt.Errorf("%w: transfer %d starts at offset %d", ErrProtocol, id, off)
		}
		if int(total) == n {
			return id, append([]byte(nil), frame[ChunkHeaderLen:]...), true, nil
		}
		if len(r.parts) >= MaxPartial {
			return id, nil, false, fmt.Errorf("%w: more than %d partial transfers", ErrProtocol, MaxPartial)
		}
		p = &partial{total: int(total), buf: make([]byte, 0, total)}
		r.parts[id] = p
	} else {
		if int(total) != p.total {
			return id, nil, false, fmt.Errorf("%w: transfer %d total_len changed %d -> %d", ErrProtocol, id, p.total, total)
		}
		if int(off) != len(p.buf) {
			return id, nil, false, fmt.Errorf("%w: transfer %d offset %d, expected %d", ErrProtocol, id, off, len(p.buf))
		}
	}
	p.buf = append(p.buf, frame[ChunkHeaderLen:]...)
	if len(p.buf) == p.total {
		delete(r.parts, id)
		return id, p.buf, true, nil
	}
	return id, nil, false, nil
}
