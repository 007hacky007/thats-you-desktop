//go:build !windows

package pagehost

import (
	"os/exec"
	"strconv"
	"strings"
	"syscall"
)

// processPath is the executable of a running process ("" if unknown).
func processPath(pid int) string {
	out, err := exec.Command("ps", "-p", strconv.Itoa(pid), "-o", "comm=").Output()
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(out))
}

// hideWindow puts cloudflared in its own process group so Stop can end it with its children.
func hideWindow(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true}
}

// attachChild has nothing to do here: the process group is enough for Stop.
func attachChild(cmd *exec.Cmd) {}

func killTree(cmd *exec.Cmd) {
	if cmd.Process == nil {
		return
	}
	syscall.Kill(-cmd.Process.Pid, syscall.SIGKILL)
	cmd.Process.Kill()
}
