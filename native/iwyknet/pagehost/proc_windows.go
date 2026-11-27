//go:build windows

package pagehost

import (
	"os/exec"
	"sync"
	"syscall"
	"unsafe"
)

const createNoWindow = 0x08000000

// hideWindow keeps cloudflared from opening a console window over the game.
func hideWindow(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
}

var (
	jobOnce sync.Once
	job     syscall.Handle

	kernel32                     = syscall.NewLazyDLL("kernel32.dll")
	procCreateJobObjectW         = kernel32.NewProc("CreateJobObjectW")
	procSetInformationJobObject  = kernel32.NewProc("SetInformationJobObject")
	procAssignProcessToJobObject = kernel32.NewProc("AssignProcessToJobObject")
)

// jobObjectExtendedLimitInformation with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: when the game exits,
// even by crashing, Windows closes the job handle and ends cloudflared with it.
type basicLimit struct {
	PerProcessUserTimeLimit int64
	PerJobUserTimeLimit     int64
	LimitFlags              uint32
	MinimumWorkingSetSize   uintptr
	MaximumWorkingSetSize   uintptr
	ActiveProcessLimit      uint32
	Affinity                uintptr
	PriorityClass           uint32
	SchedulingClass         uint32
}

type ioCounters struct{ a, b, c, d, e, f uint64 }

type extendedLimit struct {
	Basic                 basicLimit
	IO                    ioCounters
	ProcessMemoryLimit    uintptr
	JobMemoryLimit        uintptr
	PeakProcessMemoryUsed uintptr
	PeakJobMemoryUsed     uintptr
}

const (
	jobObjectExtendedLimitInformationClass = 9
	killOnJobClose                         = 0x2000
	processAllAccess                       = 0x1F0FFF
)

func theJob() syscall.Handle {
	jobOnce.Do(func() {
		h, _, _ := procCreateJobObjectW.Call(0, 0)
		if h == 0 {
			return
		}
		info := extendedLimit{}
		info.Basic.LimitFlags = killOnJobClose
		procSetInformationJobObject.Call(h, jobObjectExtendedLimitInformationClass,
			uintptr(unsafe.Pointer(&info)), unsafe.Sizeof(info))
		job = syscall.Handle(h)
	})
	return job
}

// attachChild ties cloudflared to the game's lifetime.
func attachChild(cmd *exec.Cmd) {
	j := theJob()
	if j == 0 || cmd.Process == nil {
		return
	}
	ph, err := syscall.OpenProcess(processAllAccess, false, uint32(cmd.Process.Pid))
	if err != nil {
		return
	}
	defer syscall.CloseHandle(ph)
	procAssignProcessToJobObject.Call(uintptr(j), uintptr(ph))
}

func killTree(cmd *exec.Cmd) {
	if cmd.Process != nil {
		cmd.Process.Kill()
	}
}
