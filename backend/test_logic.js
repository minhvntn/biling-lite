const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();
async function run() {
    const member = await prisma.member.findFirst();
    if (!member) { console.log('No member'); return; }
    
    try {
        const ranks = await prisma.loyaltyRankConfig.findMany({ orderBy: { minTopup: "asc" } });
        let rankIndex = 0;
        for (let i = 0; i < ranks.length; i++) {
          if (Number(member.totalTopup) >= Number(ranks[i].minTopup)) rankIndex = i;
        }
        const maxUnlocked = Math.min(10, rankIndex + 1);
        const availableAvatars = [];
        const styles = ["", "_bottts", "_micah", "_adventurer-neutral", "_lol", "_roblox"];
        for (const style of styles) {
          for (let i = 1; i <= maxUnlocked; i++) {
            availableAvatars.push(`avatar${style}_${i}.png`);
          }
        }
        console.log({ currentAvatarId: member.avatarId || 'avatar_1.png', availableAvatars });
    } catch (e) {
        console.error("ERROR", e);
    } finally {
        await prisma.$disconnect();
    }
}
run();
