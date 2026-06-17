const fs = require('fs');
const path = require('path');

function replaceInDir(dir) {
    const files = fs.readdirSync(dir);
    for (const file of files) {
        const fullPath = path.join(dir, file);
        if (fs.statSync(fullPath).isDirectory()) {
            replaceInDir(fullPath);
        } else if (fullPath.endsWith('.cs') && !fullPath.endsWith('WindowExtensions.cs') && !fullPath.endsWith('ColorSettingsWindow.xaml.cs')) {
            let content = fs.readFileSync(fullPath, 'utf-8');
            let original = content;
            
            // Replace .ShowDialog() with .ShowModal() for standard windows.
            // Exclude common dialogs like OpenFileDialog or ColorDialog if we can, 
            // but ShowModal works for any Window. Since OpenFileDialog is not a Window, it might cause compile errors if we call ShowModal on it!
            // Let's be careful. Let's use regex to find where dialog is a Window.
            // Actually, we can just look for the instances we know:
            // "dialog.ShowDialog()" -> "dialog.ShowModal(this)"
            // "addWindow.ShowDialog()" -> "addWindow.ShowModal()"
            // "editWindow.ShowDialog()" -> "editWindow.ShowModal()"
            // "win.ShowDialog()" -> "win.ShowModal(this)"
            // "popup.ShowDialog()" -> "popup.ShowModal()"

            const replacements = [
                { from: 'dialog.ShowDialog()', to: 'dialog.ShowModal(this)' },
                { from: 'addWindow.ShowDialog()', to: 'addWindow.ShowModal()' },
                { from: 'editWindow.ShowDialog()', to: 'editWindow.ShowModal()' },
                { from: 'win.ShowDialog()', to: 'win.ShowModal(this)' },
                { from: 'popup.ShowDialog()', to: 'popup.ShowModal()' },
            ];
            
            for (const r of replacements) {
                // simple global string replace
                content = content.split(r.from).join(r.to);
            }
            
            if (content !== original) {
                fs.writeFileSync(fullPath, content, 'utf-8');
                console.log(`Updated ${fullPath}`);
            }
        }
    }
}

replaceInDir('i:/servermanagerbilling/server-admin-app/MainWindow');
replaceInDir('i:/servermanagerbilling/server-admin-app/Windows');
console.log('Done');
