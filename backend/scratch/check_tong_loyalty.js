const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const m = await prisma.member.findFirst({ where: { username: 'tong' } });
  if (!m) {
    console.log('Member tong not found');
    return;
  }
  console.log('Member:', m.id, m.username, 'playSeconds:', m.playSeconds);

  const txs = await prisma.memberTransaction.findMany({
    where: { memberId: m.id },
    orderBy: { createdAt: 'desc' },
    take: 15
  });
  console.log('Recent Txs:');
  for (const t of txs) {
    console.log(`- [${t.createdAt.toISOString()}] type=${t.type}, delta=${t.playSecondsDelta}, createdBy=${t.createdBy}, note=${t.note}`);
  }
}

main().finally(() => prisma.$disconnect());
