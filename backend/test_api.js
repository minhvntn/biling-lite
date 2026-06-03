const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();
async function run() {
    const member = await prisma.member.findFirst();
    if (!member) { console.log('No member'); return; }
    console.log('Member ID:', member.id);
    
    const http = require('http');
    http.get(`http://127.0.0.1:3000/api/v1/members/${member.id}/avatars`, (res) => {
        let data = '';
        res.on('data', chunk => data += chunk);
        res.on('end', () => console.log('Response:', res.statusCode, data));
    }).on('error', console.error);
}
run();
