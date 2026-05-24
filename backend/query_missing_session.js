const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const session = await prisma.session.findFirst({
    where: {
      startedAt: {
        gte: new Date('2026-05-24T10:10:00.000Z'),
        lte: new Date('2026-05-24T10:20:00.000Z')
      }
    }
  });
  console.dir(session, { depth: null });
}

main().finally(() => prisma.$disconnect());
