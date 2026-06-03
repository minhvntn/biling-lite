const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function run() {
  const promos = await prisma.timeBasedPromotion.findMany();
  console.log('Promotions:', promos);
  
  const sessions = await prisma.session.findMany({
    where: { status: 'ACTIVE' },
    orderBy: { startedAt: 'desc' },
    take: 1
  });
  console.log('Active Session:', sessions);
  
  const pc = await prisma.pc.findFirst({
    where: { id: sessions[0].pcId },
    include: { group: true }
  });
  console.log('PC:', pc);
}

run().finally(() => prisma.$disconnect());
