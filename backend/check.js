const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function check() {
  const m1 = await prisma.member.findFirst({
    where: { username: { contains: 'test95b14f46' } }
  });
  console.log('Member 1:', m1);
  
  const tx = await prisma.memberTransaction.findMany({
    where: { memberId: m1.id }
  });
  console.log('Transactions:', tx);
}

check().finally(() => prisma.$disconnect());
