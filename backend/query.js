const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function run() {
  const member = await prisma.member.findFirst({
    where: { username: 'tong' }
  });
  console.log('Member tong:', member);

  const pc = await prisma.pc.findFirst({
    where: { name: 'PC' },
    include: { group: true }
  });
  console.log('PC:', pc);

  const activeSessions = await prisma.session.findMany({
    where: { status: 'ACTIVE' },
    include: { pc: true }
  });
  console.log('Active Sessions:', activeSessions);
}

run().finally(() => prisma.$disconnect());
