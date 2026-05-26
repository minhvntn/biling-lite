const { exec } = require("child_process");

const port = process.argv[2];

if (!port) {
    console.log("Usage: node kill-port.js <port>");
    process.exit(1);
}

const isWin = process.platform === "win32";

if (isWin) {
    // Windows
    exec(`netstat -ano | findstr :${port}`, (err, stdout) => {
        if (err || !stdout) {
            console.log(`No process found using port ${port}`);
            return;
        }

        const lines = stdout.trim().split("\n");

        const pids = new Set();

        lines.forEach((line) => {
            const parts = line.trim().split(/\s+/);
            const pid = parts[parts.length - 1];

            if (pid && pid !== "0") {
                pids.add(pid);
            }
        });

        if (pids.size === 0) {
            console.log(`No PID found for port ${port}`);
            return;
        }

        pids.forEach((pid) => {
            exec(`taskkill /PID ${pid} /F`, (killErr) => {
                if (killErr) {
                    console.log(`Failed to kill PID ${pid}`);
                } else {
                    console.log(`Killed PID ${pid} on port ${port}`);
                }
            });
        });
    });
} else {
    // Linux / macOS
    exec(`lsof -ti:${port}`, (err, stdout) => {
        if (err || !stdout) {
            console.log(`No process found using port ${port}`);
            return;
        }

        const pids = stdout.trim().split("\n");

        pids.forEach((pid) => {
            exec(`kill -9 ${pid}`, (killErr) => {
                if (killErr) {
                    console.log(`Failed to kill PID ${pid}`);
                } else {
                    console.log(`Killed PID ${pid} on port ${port}`);
                }
            });
        });
    });
}