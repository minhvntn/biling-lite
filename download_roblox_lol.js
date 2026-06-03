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
    // 1. League of Legends
    const lolChamps = ['Ahri', 'Yasuo', 'LeeSin', 'Jinx', 'Thresh', 'Zed', 'Vayne', 'Lux', 'Darius', 'Teemo'];
    console.log('Downloading League of Legends avatars...');
    for (let i = 0; i < lolChamps.length; i++) {
        const url = `https://ddragon.leagueoflegends.com/cdn/14.3.1/img/champion/${lolChamps[i]}.png`;
        const dest = path.join(avatarsDir, `avatar_lol_${i + 1}.png`);
        try {
            await download(url, dest);
            console.log(`Downloaded LoL: ${lolChamps[i]}`);
        } catch (e) {
            console.error(e.message);
        }
    }

    // 2. Roblox
    console.log('Downloading Roblox avatars...');
    // We get 10 valid active users by fetching a few random IDs and checking if they exist
    const userIds = [1, 2, 3, 156, 16, 18, 40, 261, 144, 53, 11, 20, 30, 40, 50, 60, 70, 80, 90, 100].join(',');
    const url = `https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=${userIds}&size=150x150&format=Png&isCircular=false`;
    try {
        const json = await downloadJson(url);
        const validData = json.data.filter(d => d.state === 'Completed' && d.imageUrl).slice(0, 10);
        for (let i = 0; i < validData.length; i++) {
            const imgUrl = validData[i].imageUrl;
            const dest = path.join(avatarsDir, `avatar_roblox_${i + 1}.png`);
            await download(imgUrl, dest);
            console.log(`Downloaded Roblox: ID ${validData[i].targetId}`);
        }
    } catch (e) {
        console.error('Failed to download Roblox avatars:', e.message);
    }

    console.log('Done!');
}

run().catch(console.error);
