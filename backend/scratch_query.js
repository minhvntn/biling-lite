const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const members = await prisma.member.findMany({
    where: { playSeconds: { gt: 0 } },
  });
  console.log('Members with playSeconds:', members.map(m => ({ username: m.username, playSeconds: m.playSeconds, balance: m.balance })));
}

main().finally(() => prisma.$disconnect());
