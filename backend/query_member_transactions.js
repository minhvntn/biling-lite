const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const member = await prisma.member.findFirst({
    where: { username: 'teo' }
  });
  if (!member) {
    console.log('Member teo not found');
    return;
  }

  const txs = await prisma.memberTransaction.findMany({
    where: { memberId: member.id },
    orderBy: { createdAt: 'desc' },
    take: 10
  });

  console.log(`Transactions for teo (Current Balance: ${member.balance}):`);
  console.log(JSON.stringify(txs, null, 2));
}

main().finally(() => prisma.$disconnect());
