const fs = require('fs');
const https = require('https');
const path = require('path');

const download = (url, dest) => {
    return new Promise((resolve, reject) => {
        const file = fs.createWriteStream(dest);
        https.get(url, (response) => {
            if (response.statusCode !== 200) {
                reject(new Error(`Failed to download: ${response.statusCode}`));
                return;
            }
            response.pipe(file);
            file.on('finish', () => {
                file.close();
                resolve();
            });
        }).on('error', (err) => {
            fs.unlink(dest, () => {});
            reject(err);
        });
    });
};

const styles = ['bottts', 'micah', 'adventurer-neutral'];
const avatarsDir = 'client/src/Client.Agent.Wpf/Assets/Avatars';

async function run() {
    for (const style of styles) {
        for (let i = 1; i <= 10; i++) {
            // Dicebear returns larger images, we will request size=70
            const url = `https://api.dicebear.com/7.x/${style}/png?seed=${style}${i}&size=70`;
            const dest = path.join(avatarsDir, `avatar_${style}_${i}.png`);
            console.log(`Downloading ${url} to ${dest}...`);
            await download(url, dest);
        }
    }
    console.log('All downloaded.');
}

run().catch(console.error);
