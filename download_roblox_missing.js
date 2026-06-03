const fs = require('fs');
const https = require('https');
const path = require('path');

const download = (url, dest) => {
    return new Promise((resolve, reject) => {
        const file = fs.createWriteStream(dest);
        https.get(url, (response) => {
            if (response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
                return download(response.headers.location, dest).then(resolve).catch(reject);
            }
            if (response.statusCode !== 200) {
                reject(new Error(`Failed to download ${url}: ${response.statusCode}`));
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

const downloadJson = (url) => {
    return new Promise((resolve, reject) => {
        https.get(url, (response) => {
            let data = '';
            response.on('data', chunk => data += chunk);
            response.on('end', () => {
                try { resolve(JSON.parse(data)); } catch (e) { reject(e); }
            });
        }).on('error', reject);
    });
};

const avatarsDir = 'client/src/Client.Agent.Wpf/Assets/Avatars';

async function run() {
    let validData = [];
    let startId = 1000;
    while (validData.length < 3) {
        const userIds = Array.from({length: 20}, (_, i) => startId + i).join(',');
        const url = `https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=${userIds}&size=150x150&format=Png&isCircular=false`;
        const json = await downloadJson(url);
        validData.push(...json.data.filter(d => d.state === 'Completed' && d.imageUrl));
        startId += 20;
    }
    
    for (let i = 0; i < 3; i++) {
        const imgUrl = validData[i].imageUrl;
        const dest = path.join(avatarsDir, `avatar_roblox_${8 + i}.png`);
        await download(imgUrl, dest);
        console.log(`Downloaded Roblox: ID ${validData[i].targetId}`);
    }
}

run().catch(console.error);
